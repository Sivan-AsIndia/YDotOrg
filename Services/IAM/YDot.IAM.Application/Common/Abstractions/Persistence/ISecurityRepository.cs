using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Sessions, refresh tokens, MFA, trusted devices and sign-in history — everything behind
/// IAM-USR-04 and the authentication flows.
///
/// A NOTE ON WHY SO MANY LOOKUPS TAKE A HASH. Nothing here is ever queried by a plaintext
/// secret, because no plaintext secret is stored. The caller hashes what it was given and
/// looks the hash up, which is both the security property and, incidentally, an index seek.
/// </summary>
public interface ISecurityRepository
{
    // ---- Sessions ---------------------------------------------------------------------

    Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<UserSession?> GetSessionByHashAsync(string sessionTokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> GetActiveSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every session including the closed ones, for the security screen history.</summary>
    Task<IReadOnlyList<UserSession>> GetSessionHistoryAsync(Guid userId, int take, CancellationToken cancellationToken);

    Task AddSessionAsync(UserSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every live session for a user, optionally sparing the one making the request
    /// so "sign out my other devices" does not sign the person out of the device they are
    /// holding.
    /// </summary>
    Task<int> RevokeAllSessionsAsync(
        Guid userId, Guid? exceptSessionId, string reason, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the oldest sessions once a user is over the concurrent ceiling, so an account
    /// cannot accumulate a hundred live sessions across abandoned devices.
    /// </summary>
    Task<int> TrimSessionsAsync(Guid userId, int keepNewest, DateTimeOffset asOf, CancellationToken cancellationToken);

    // ---- Refresh tokens ---------------------------------------------------------------------

    Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Kills an entire rotation chain. Called when an already-consumed token is presented
    /// again: two parties hold the same token, so every descendant of it is destroyed rather
    /// than just the one request refused.
    /// </summary>
    Task<int> RevokeTokenChainAsync(Guid sessionId, string reason, DateTimeOffset asOf, CancellationToken cancellationToken);

    // ---- MFA -------------------------------------------------------------------------------------

    Task<IReadOnlyList<MfaMethod>> GetMfaMethodsAsync(Guid userId, CancellationToken cancellationToken);

    Task<MfaMethod?> GetMfaMethodAsync(Guid methodId, CancellationToken cancellationToken);

    /// <summary>The method challenged by default when several are enrolled.</summary>
    Task<MfaMethod?> GetPrimaryMfaMethodAsync(Guid userId, CancellationToken cancellationToken);

    Task AddMfaMethodAsync(MfaMethod method, CancellationToken cancellationToken);

    Task<MfaChallenge?> GetChallengeAsync(string challengeToken, CancellationToken cancellationToken);

    Task AddChallengeAsync(MfaChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// The newest live challenge of one purpose for one user.
    ///
    /// Needed by the enrolment path, where the person has the CODE but not the challenge token:
    /// a code arrives in an e-mail or a text and the screen posts it back with the method id,
    /// not with an opaque handle it was never given. Looking the challenge up here keeps the
    /// attempt ceiling, the expiry and the single-use rule in the one place that enforces them,
    /// rather than tempting the caller into comparing hashes itself and losing all three.
    /// </summary>
    Task<MfaChallenge?> GetLatestChallengeAsync(
        Guid userId,
        Domain.Enums.MfaChallengePurpose purpose,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>Retires any outstanding challenge of the same purpose before a new one is issued.</summary>
    Task<int> ConsumeOpenChallengesAsync(
        Guid userId, Domain.Enums.MfaChallengePurpose purpose, DateTimeOffset asOf, CancellationToken cancellationToken);

    // ---- Recovery codes --------------------------------------------------------------------------------

    Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken);

    Task<RecoveryCode?> FindRedeemableRecoveryCodeAsync(Guid userId, string codeHash, CancellationToken cancellationToken);

    Task AddRecoveryCodesAsync(IEnumerable<RecoveryCode> codes, CancellationToken cancellationToken);

    /// <summary>Retires the previous batch when a new set is generated.</summary>
    Task<int> RetireRecoveryCodesAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken);

    // ---- Trusted devices ----------------------------------------------------------------------------------

    Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken);

    Task<TrustedDevice?> GetTrustedDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    Task<TrustedDevice?> FindTrustedDeviceAsync(Guid userId, string deviceTokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// The live remembered-device row for a browser that presented no cookie, matched on the
    /// stable identifier the client mints for itself.
    ///
    /// Exists so "remember this device" renews one row per browser rather than adding a new one
    /// every time the cookie has been cleared — a list of eight entries all naming the same
    /// laptop is exactly as useless as an empty one.
    /// </summary>
    Task<TrustedDevice?> FindTrustedDeviceByIdentifierAsync(
        Guid userId, string? deviceIdentifier, DateTimeOffset asOf, CancellationToken cancellationToken);

    Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken cancellationToken);

    Task<int> RevokeAllTrustedDevicesAsync(Guid userId, string reason, DateTimeOffset asOf, CancellationToken cancellationToken);

    // ---- Recovery tokens (password reset, e-mail confirmation) ------------------------------------------------

    Task<RecoveryToken?> GetRecoveryTokenAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddRecoveryTokenAsync(RecoveryToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates outstanding tokens of one purpose before issuing a new one, so a mailbox
    /// full of old reset links has exactly one that works.
    /// </summary>
    Task<int> InvalidateRecoveryTokensAsync(
        Guid userId, Domain.Enums.RecoveryTokenPurpose purpose, string reason, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>How many reset requests this address has made recently, for rate limiting.</summary>
    Task<int> CountRecentRecoveryRequestsAsync(
        Guid userId, Domain.Enums.RecoveryTokenPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken);

    // ---- Sign-in attempts -----------------------------------------------------------------------------------------

    Task AddSignInAttemptAsync(SignInAttempt attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignInAttempt>> GetRecentAttemptsAsync(Guid userId, int take, CancellationToken cancellationToken);

    /// <summary>Attempts from one address in a window, for IP-level rate limiting.</summary>
    Task<int> CountRecentAttemptsByIpAsync(string ipAddress, DateTimeOffset since, CancellationToken cancellationToken);

    // ---- Step-up drafts ----------------------------------------------------------------------------------------------

    Task<ProtectedActionDraft?> GetDraftAsync(string draftToken, CancellationToken cancellationToken);

    Task AddDraftAsync(ProtectedActionDraft draft, CancellationToken cancellationToken);
}
