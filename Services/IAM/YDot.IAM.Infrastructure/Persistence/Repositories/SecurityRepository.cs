using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// Sessions, refresh tokens, MFA, devices, recovery tokens and sign-in history.
///
/// SEVERAL METHODS HERE BYPASS THE QUERY FILTER, and every one of them is on an
/// unauthenticated path: verifying an MFA challenge, redeeming a reset link, exchanging a
/// refresh token. At those moments there is no ambient Organisation because the caller has
/// not proved anything yet — the row itself carries the Organisation, and the flow acts on
/// THAT rather than on anything the caller supplied.
///
/// The lookup key in each case is a hash of a secret only the right person holds, which is
/// what makes bypassing the filter safe: possession of the token is the authorisation.
/// </summary>
// =============================================================================================
// WHY SO MANY READS HERE LIFT THE QUERY FILTER.
//
// Every one of them is keyed on a UserId, and the caller has already been authorised for that
// user by the query that resolved them — which IS filtered. `UserId == userId` is therefore a
// tighter boundary than the tenant filter, not a looser one, and nothing below takes an id
// straight from a request.
//
// Leaving the filter on was wrong in a way that was easy to miss: a SuperAdmin's own sessions,
// devices and factors carry the platform sentinel rather than an Organisation id, so while they
// operated inside TEN001 their own security page came back empty and they could not end a
// session on a lost phone without leaving the Organisation first.
//
// This is the "defence in depth, not the only mechanism" case from the brief: the authorization
// is the user lookup, and the filter is the belt that has to come off once the braces are on.
// =============================================================================================

public sealed class SecurityRepository(IamDbContext context) : ISecurityRepository
{
    // =================================================================================
    // Sessions
    // =================================================================================

    public Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.UserSessions
            .IgnoreQueryFilters()
            .Include(session => session.User)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public Task<UserSession?> GetSessionByHashAsync(
        string sessionTokenHash, CancellationToken cancellationToken) =>
        context.UserSessions
            .IgnoreQueryFilters()
            .Include(session => session.User)
            .FirstOrDefaultAsync(session => session.SessionTokenHash == sessionTokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return await context.UserSessions
            .IgnoreQueryFilters()
            .Where(session => session.UserId == userId
                              && session.RevokedAtUtc == null
                              && session.ExpiresAtUtc > now)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> GetSessionHistoryAsync(
        Guid userId, int take, CancellationToken cancellationToken) =>
        await context.UserSessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.IssuedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

    public async Task AddSessionAsync(UserSession session, CancellationToken cancellationToken) =>
        await context.UserSessions.AddAsync(session, cancellationToken);

    /// <summary>
    /// Revokes every live session for a user.
    ///
    /// <paramref name="exceptSessionId"/> spares the one making the request, so "sign out my
    /// other devices" does not sign the person out of the device they are holding.
    /// </summary>
    public async Task<int> RevokeAllSessionsAsync(
        Guid userId, Guid? exceptSessionId, string reason, DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var sessions = await context.UserSessions
            .IgnoreQueryFilters()
            .Where(session => session.UserId == userId
                              && session.RevokedAtUtc == null
                              && (exceptSessionId == null || session.Id != exceptSessionId))
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAtUtc = asOf;
            session.RevocationReason = reason;
        }

        // The refresh tokens go with them. Leaving a live refresh token behind would let the
        // session be rebuilt moments after it was revoked.
        var sessionIds = sessions.Select(session => session.Id).ToList();

        if (sessionIds.Count > 0)
        {
            var tokens = await context.RefreshTokens
                .IgnoreQueryFilters()
                .Where(token => sessionIds.Contains(token.SessionId) && token.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                token.RevokedAtUtc = asOf;
                token.RevocationReason = reason;
            }
        }

        return sessions.Count;
    }

    /// <summary>
    /// Closes the oldest sessions once a user is over the concurrent ceiling.
    ///
    /// Without this an account accumulates a live session per abandoned device forever, and
    /// "sign out everywhere" becomes the only way to clean up.
    /// </summary>
    public async Task<int> TrimSessionsAsync(
        Guid userId, int keepNewest, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var excess = await context.UserSessions
            .IgnoreQueryFilters()
            .Where(session => session.UserId == userId
                              && session.RevokedAtUtc == null
                              && session.ExpiresAtUtc > asOf)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .Skip(Math.Max(1, keepNewest))
            .ToListAsync(cancellationToken);

        foreach (var session in excess)
        {
            session.RevokedAtUtc = asOf;
            session.RevocationReason = "Exceeded the maximum number of concurrent sessions.";
        }

        return excess.Count;
    }

    // =================================================================================
    // Refresh tokens
    // =================================================================================

    public Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens
            .IgnoreQueryFilters()
            .Include(token => token.Session)
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken) =>
        await context.RefreshTokens.AddAsync(token, cancellationToken);

    /// <summary>
    /// Destroys a whole rotation chain, and the session behind it.
    ///
    /// Called when an already-consumed token is presented again. That means two parties hold
    /// the same token, so the correct response is not to refuse one request but to end the
    /// session entirely — whichever of the two is the attacker, neither gets to continue.
    /// </summary>
    public async Task<int> RevokeTokenChainAsync(
        Guid sessionId, string reason, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var tokens = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.SessionId == sessionId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = asOf;
            token.RevocationReason = reason;
        }

        var session = await context.UserSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);

        if (session is not null && session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = asOf;
            session.RevocationReason = reason;
        }

        return tokens.Count;
    }

    // =================================================================================
    // MFA
    // =================================================================================

    public async Task<IReadOnlyList<MfaMethod>> GetMfaMethodsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.MfaMethods
            .IgnoreQueryFilters()
            .Where(method => method.UserId == userId && method.Status != MfaMethodStatus.Revoked)
            .OrderByDescending(method => method.IsPrimary)
            .ThenBy(method => method.MethodType)
            .ToListAsync(cancellationToken);

    public Task<MfaMethod?> GetMfaMethodAsync(Guid methodId, CancellationToken cancellationToken) =>
        context.MfaMethods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(method => method.Id == methodId, cancellationToken);

    public Task<MfaMethod?> GetPrimaryMfaMethodAsync(Guid userId, CancellationToken cancellationToken) =>
        context.MfaMethods
            .IgnoreQueryFilters()
            .Where(method => method.UserId == userId && method.Status == MfaMethodStatus.Active)
            .OrderByDescending(method => method.IsPrimary)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddMfaMethodAsync(MfaMethod method, CancellationToken cancellationToken) =>
        await context.MfaMethods.AddAsync(method, cancellationToken);

    /// <summary>
    /// Resolves a challenge from its opaque handle.
    ///
    /// Filters bypassed: the person verifying a code has passed the password check but holds
    /// no token yet, so there is no ambient Organisation. The challenge row names both the
    /// user and the Organisation.
    /// </summary>
    public Task<MfaChallenge?> GetChallengeAsync(string challengeToken, CancellationToken cancellationToken) =>
        context.MfaChallenges
            .IgnoreQueryFilters()
            .Include(challenge => challenge.User)
            .Include(challenge => challenge.MfaMethod)
            .FirstOrDefaultAsync(challenge => challenge.ChallengeToken == challengeToken, cancellationToken);

    public async Task AddChallengeAsync(MfaChallenge challenge, CancellationToken cancellationToken) =>
        await context.MfaChallenges.AddAsync(challenge, cancellationToken);

    /// <summary>
    /// The newest live challenge of one purpose for one user.
    ///
    /// IgnoreQueryFilters for the same reason GetChallengeAsync does: enrolment during
    /// activation happens with no session and therefore no ambient Organisation. The filter is
    /// re-applied by hand as the UserId predicate — the caller has already resolved that user
    /// from an invitation token or a signed session, so it is not a value a client can assert.
    /// </summary>
    public Task<MfaChallenge?> GetLatestChallengeAsync(
        Guid userId,
        MfaChallengePurpose purpose,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        context.MfaChallenges
            .IgnoreQueryFilters()
            .Include(challenge => challenge.User)
            .Where(challenge => challenge.UserId == userId
                                && challenge.Purpose == purpose
                                && !challenge.IsConsumed
                                && challenge.ExpiresAtUtc > asOf)
            .OrderByDescending(challenge => challenge.IssuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Retires any outstanding challenge of the same purpose before a new one is issued, so
    /// only the newest code works. Otherwise a person with three codes in their inbox has
    /// three valid ones, and the oldest may be the one an attacker intercepted.
    /// </summary>
    public async Task<int> ConsumeOpenChallengesAsync(
        Guid userId, MfaChallengePurpose purpose, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var open = await context.MfaChallenges
            .IgnoreQueryFilters()
            .Where(challenge => challenge.UserId == userId
                                && challenge.Purpose == purpose
                                && !challenge.IsConsumed)
            .ToListAsync(cancellationToken);

        foreach (var challenge in open)
        {
            challenge.IsConsumed = true;
        }

        return open.Count;
    }

    // =================================================================================
    // Recovery codes
    // =================================================================================

    public async Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.RecoveryCodes
            .IgnoreQueryFilters()
            .Where(code => code.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task<RecoveryCode?> FindRedeemableRecoveryCodeAsync(
        Guid userId, string codeHash, CancellationToken cancellationToken) =>
        context.RecoveryCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                code => code.UserId == userId
                        && code.CodeHash == codeHash
                        && code.RedeemedAtUtc == null
                        && code.RetiredAtUtc == null,
                cancellationToken);

    public async Task AddRecoveryCodesAsync(
        IEnumerable<RecoveryCode> codes, CancellationToken cancellationToken) =>
        await context.RecoveryCodes.AddRangeAsync(codes, cancellationToken);

    public async Task<int> RetireRecoveryCodesAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var live = await context.RecoveryCodes
            .IgnoreQueryFilters()
            .Where(code => code.UserId == userId && code.RedeemedAtUtc == null && code.RetiredAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var code in live)
        {
            code.RetiredAtUtc = asOf;
        }

        return live.Count;
    }

    // =================================================================================
    // Trusted devices
    // =================================================================================

    public async Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.TrustedDevices
            .IgnoreQueryFilters()
            .Where(device => device.UserId == userId && device.RevokedAtUtc == null)
            .OrderByDescending(device => device.LastSeenAtUtc ?? device.TrustedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<TrustedDevice?> GetTrustedDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.TrustedDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(device => device.Id == deviceId, cancellationToken);

    /// <summary>Filters bypassed: this runs during sign-in, before a token exists.</summary>
    public Task<TrustedDevice?> FindTrustedDeviceAsync(
        Guid userId, string deviceTokenHash, CancellationToken cancellationToken) =>
        context.TrustedDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                device => device.UserId == userId
                          && device.DeviceTokenHash == deviceTokenHash
                          && device.RevokedAtUtc == null,
                cancellationToken);

    /// <summary>
    /// The live row for a browser that presented no cookie. Filters bypassed for the same reason
    /// as the lookup above: this runs during sign-in, before a token exists.
    /// </summary>
    public Task<TrustedDevice?> FindTrustedDeviceByIdentifierAsync(
        Guid userId, string? deviceIdentifier, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            return Task.FromResult<TrustedDevice?>(null);
        }

        return context.TrustedDevices
            .IgnoreQueryFilters()
            .OrderByDescending(device => device.TrustedAtUtc)
            .FirstOrDefaultAsync(
                device => device.UserId == userId
                          && device.DeviceIdentifier == deviceIdentifier
                          && device.RevokedAtUtc == null
                          && device.ExpiresAtUtc > asOf,
                cancellationToken);
    }

    public async Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken cancellationToken) =>
        await context.TrustedDevices.AddAsync(device, cancellationToken);

    public async Task<int> RevokeAllTrustedDevicesAsync(
        Guid userId, string reason, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var devices = await context.TrustedDevices
            .IgnoreQueryFilters()
            .Where(device => device.UserId == userId && device.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var device in devices)
        {
            device.RevokedAtUtc = asOf;
            device.RevocationReason = reason;
        }

        return devices.Count;
    }

    // =================================================================================
    // Recovery tokens
    // =================================================================================

    /// <summary>
    /// Resolves a reset or confirmation link.
    ///
    /// Filters bypassed because the person clicking has no session. The row names the user and
    /// the Organisation, and the flow acts on those — which is exactly what keeps a reset for
    /// TEN001 away from the same address in TEN002.
    /// </summary>
    public Task<RecoveryToken?> GetRecoveryTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RecoveryTokens
            .IgnoreQueryFilters()
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task AddRecoveryTokenAsync(RecoveryToken token, CancellationToken cancellationToken) =>
        await context.RecoveryTokens.AddAsync(token, cancellationToken);

    public async Task<int> InvalidateRecoveryTokensAsync(
        Guid userId, RecoveryTokenPurpose purpose, string reason, DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var live = await context.RecoveryTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId
                            && token.Purpose == purpose
                            && token.ConsumedAtUtc == null
                            && token.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.InvalidatedAtUtc = asOf;
            token.InvalidationReason = reason;
        }

        return live.Count;
    }

    public Task<int> CountRecentRecoveryRequestsAsync(
        Guid userId, RecoveryTokenPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        context.RecoveryTokens
            .IgnoreQueryFilters()
            .CountAsync(
                token => token.UserId == userId && token.Purpose == purpose && token.IssuedAtUtc >= since,
                cancellationToken);

    // =================================================================================
    // Sign-in attempts
    // =================================================================================

    public async Task AddSignInAttemptAsync(SignInAttempt attempt, CancellationToken cancellationToken) =>
        await context.SignInAttempts.AddAsync(attempt, cancellationToken);

    public async Task<IReadOnlyList<SignInAttempt>> GetRecentAttemptsAsync(
        Guid userId, int take, CancellationToken cancellationToken) =>
        await context.SignInAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.AttemptedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Attempts from one address in a window, for the IP rate limit.
    ///
    /// Filters bypassed because this runs before ANY account lookup — deliberately, so that
    /// probing is cheap for us to refuse and expensive for the prober.
    /// </summary>
    public Task<int> CountRecentAttemptsByIpAsync(
        string ipAddress, DateTimeOffset since, CancellationToken cancellationToken) =>
        context.SignInAttempts
            .IgnoreQueryFilters()
            .CountAsync(
                attempt => attempt.IpAddress == ipAddress && attempt.AttemptedAtUtc >= since,
                cancellationToken);

    // =================================================================================
    // Step-up drafts
    // =================================================================================

    public Task<ProtectedActionDraft?> GetDraftAsync(string draftToken, CancellationToken cancellationToken) =>
        context.ProtectedActionDrafts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(draft => draft.DraftToken == draftToken, cancellationToken);

    public async Task AddDraftAsync(ProtectedActionDraft draft, CancellationToken cancellationToken) =>
        await context.ProtectedActionDrafts.AddAsync(draft, cancellationToken);
}
