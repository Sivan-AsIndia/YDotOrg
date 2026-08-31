using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Opens, rotates and closes sessions, and mints the tokens that belong to them.
///
/// WHY THIS EXISTS RATHER THAN EACH HANDLER CALLING IJwtTokenService DIRECTLY. Issuing a
/// working session is six coordinated steps:
///
/// <code>
/// 1. resolve the effective access set
/// 2. create the session row              (the thing that can be revoked)
/// 3. mint the access token against it
/// 4. mint and hash a refresh token
/// 5. trim the user oldest sessions if they are over the ceiling
/// 6. stamp the last-login capture columns
/// </code>
///
/// Sign-in, MFA verification, invitation acceptance, refresh and Organisation switching ALL
/// need exactly that sequence. Six copies would drift, and the copy that drifted would be the
/// one that forgot step 2 — which is the one whose sessions can then never be revoked.
/// </summary>
public sealed class SessionTokenService(
    IamDbContext context,
    IJwtTokenService jwtTokenService,
    ITokenHasher tokenHasher,
    IEffectiveAccessService effectiveAccess,
    ISecurityRepository security,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<JwtSettings> jwtOptions,
    IOptions<SecuritySettings> securityOptions,
    ILogger<SessionTokenService> logger) : ISessionTokenService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<TokenResponse> IssueAsync(
        User user,
        Tenant? operatingTenant,
        BusinessUnit businessUnit,
        AccessScopeType scope,
        ClientType clientType,
        bool mfaCompleted,
        bool rememberMe,
        string? hostName,
        string? deviceIdentifier,
        string? deviceName,
        bool isTrustedDevice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = clock.UtcNow;

        // ---- 1. What may they do -------------------------------------------------------
        var access = await effectiveAccess.ResolveAsync(user, operatingTenant?.Id, cancellationToken);

        // ---- 2. The session row ----------------------------------------------------------
        //
        // Created BEFORE the token, because the token carries its id. Without the row the
        // token could never be revoked before it expired.
        var sessionToken = tokenHasher.GenerateToken();

        // "Remember me" only extends the absolute session lifetime, never the access token.
        // A long-lived access token cannot be revoked; a long-lived session can.
        var absoluteHours = rememberMe
            ? _jwt.RefreshTokenDays * 24
            : _security.SessionAbsoluteTimeoutHours;

        var session = new UserSession
        {
            TenantId = user.TenantId ?? operatingTenant?.Id ?? Guid.Empty,
            BusinessUnitId = businessUnit.Id,
            UserId = user.Id,
            SessionTokenHash = tokenHasher.Hash(sessionToken),

            // The Organisation this session works inside. For SuperAdmin it is whichever they
            // selected, and it is what lets the audit trail say where a root user was standing.
            OperatingTenantId = operatingTenant?.Id,
            AccessScope = scope,

            DeviceName = deviceName,
            DeviceIdentifier = deviceIdentifier,
            ClientType = clientType,
            UserAgent = currentUser.UserAgent,
            IpAddress = currentUser.IpAddress,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddHours(absoluteHours),
            LastActivityAtUtc = now,
            MfaCompleted = mfaCompleted,
            MfaCompletedAtUtc = mfaCompleted ? now : null,
            LastReauthenticatedAtUtc = now,
            IsTrustedDevice = isTrustedDevice
        };

        await security.AddSessionAsync(session, cancellationToken);

        // ---- 3. The access token ------------------------------------------------------------
        var accessToken = jwtTokenService.CreateAccessToken(
            user, operatingTenant, businessUnit, session.Id, scope, access,
            clientType, hostName, deviceIdentifier, mfaCompleted);

        // ---- 4. The refresh token ---------------------------------------------------------------
        var (refreshToken, refreshHash, refreshExpiry) = jwtTokenService.CreateRefreshToken();

        await security.AddRefreshTokenAsync(new RefreshToken
        {
            TenantId = session.TenantId,
            BusinessUnitId = businessUnit.Id,
            UserId = user.Id,
            SessionId = session.Id,
            TokenHash = refreshHash,
            IssuedAtUtc = now,
            ExpiresAtUtc = refreshExpiry,
            CreatedFromIpAddress = currentUser.IpAddress,
            CreatedByUserAgent = currentUser.UserAgent
        }, cancellationToken);

        // ---- 5. Keep the session count sane ------------------------------------------------------
        await security.TrimSessionsAsync(
            user.Id, _security.MaximumConcurrentSessions, now, cancellationToken);

        return new TokenResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            accessToken.ExpiresInSeconds,
            "Bearer",
            refreshToken,
            refreshExpiry,
            session.Id,
            BuildUserResponse(user, access),
            BuildTenantResponse(
                operatingTenant, businessUnit, scope,
                isTenantMode: scope == AccessScopeType.Global && operatingTenant is not null));
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair.
    ///
    /// REUSE IS TREATED AS THEFT, not as a mistake to forgive. Presenting a token that has
    /// already been consumed means two parties hold it — so the whole rotation chain and the
    /// session behind it are destroyed, and BOTH parties are signed out. Whichever one is the
    /// attacker, neither gets to continue.
    ///
    /// This is why the Angular interceptor funnels parallel 401s through a single shared
    /// refresh: six simultaneous refreshes present the same token six times and look exactly
    /// like an attack.
    /// </summary>
    public async Task<TokenResponse?> RefreshAsync(
        string refreshToken, string? hostName, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = tokenHasher.Hash(refreshToken);

        var existing = await security.GetRefreshTokenAsync(hash, cancellationToken);

        if (existing is null)
        {
            logger.LogWarning("A refresh token was presented that does not exist.");
            return null;
        }

        // ---- Reuse detection ---------------------------------------------------------------
        if (existing.ConsumedAtUtc is not null)
        {
            existing.IsReuseDetected = true;

            logger.LogWarning(
                "Refresh token reuse detected for session {SessionId}, user {UserId}. "
                + "The whole token chain and the session have been revoked.",
                existing.SessionId, existing.UserId);

            await security.RevokeTokenChainAsync(
                existing.SessionId,
                "A refresh token was presented twice, which indicates it was stolen.",
                now, cancellationToken);

            return null;
        }

        if (!existing.IsRedeemable(now))
        {
            return null;
        }

        var session = existing.Session
                      ?? await security.GetSessionAsync(existing.SessionId, cancellationToken);

        if (session is null || !session.IsActive(now))
        {
            return null;
        }

        var user = existing.User
                   ?? await context.Users.IgnoreQueryFilters()
                       .FirstOrDefaultAsync(item => item.Id == existing.UserId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        // A suspended or deactivated account must not be able to refresh its way back in.
        if (user.Status is not (UserStatus.Active or UserStatus.Invited))
        {
            await security.RevokeTokenChainAsync(
                session.Id, "The account is no longer active.", now, cancellationToken);

            return null;
        }

        var tenant = session.OperatingTenantId.HasValue
            ? await context.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == session.OperatingTenantId.Value, cancellationToken)
            : null;

        // ---- May this session continue? --------------------------------------------------------
        //
        // WHY THERE IS A SECOND CHECK AT ALL. An access token lasts minutes, and the browser
        // silently trades a refresh token for a new one when it expires. Re-asking the question
        // here is what makes a suspension bite within one token lifetime instead of whenever the
        // person next happens to sign in.
        //
        // IT MUST ASK THE SAME QUESTION SIGN-IN ASKED. It used to ask a stricter one - "is the
        // Organisation Active?" - and the mismatch had a very confusing symptom: the TenantAdmin
        // of an Organisation still filling in its profile was allowed to sign in, then thrown out
        // a few minutes later in the middle of a form, over and over. Both paths now call
        // Tenant.PermitsSession, so they cannot drift apart again.
        if (tenant is not null && !user.IsSuperAdmin && !tenant.PermitsSession(user.IsTenantAdmin))
        {
            await security.RevokeTokenChainAsync(
                session.Id, "The organisation is no longer active.", now, cancellationToken);

            return null;
        }

        var businessUnit = await context.BusinessUnits
            .FirstOrDefaultAsync(unit => unit.Id == session.BusinessUnitId, cancellationToken);

        if (businessUnit is null)
        {
            return null;
        }

        // ---- Rotate ------------------------------------------------------------------------------
        existing.ConsumedAtUtc = now;

        var (newToken, newHash, newExpiry) = jwtTokenService.CreateRefreshToken();

        var replacement = new RefreshToken
        {
            TenantId = session.TenantId,
            BusinessUnitId = session.BusinessUnitId,
            UserId = user.Id,
            SessionId = session.Id,
            TokenHash = newHash,
            IssuedAtUtc = now,
            ExpiresAtUtc = newExpiry,
            CreatedFromIpAddress = currentUser.IpAddress,
            CreatedByUserAgent = currentUser.UserAgent
        };

        await security.AddRefreshTokenAsync(replacement, cancellationToken);

        // The chain link, which is what makes reuse detectable at all.
        existing.ReplacedByTokenId = replacement.Id;

        session.LastActivityAtUtc = now;

        // Access is re-resolved rather than reused, so a role removed a minute ago is gone
        // from the new token.
        var access = await effectiveAccess.ResolveAsync(user, tenant?.Id, cancellationToken);

        var accessToken = jwtTokenService.CreateAccessToken(
            user, tenant, businessUnit, session.Id, session.AccessScope, access,
            session.ClientType, hostName ?? session.UserAgent, session.DeviceIdentifier,
            session.MfaCompleted);

        return new TokenResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            accessToken.ExpiresInSeconds,
            "Bearer",
            newToken,
            newExpiry,
            session.Id,
            BuildUserResponse(user, access),
            BuildTenantResponse(
                tenant, businessUnit, session.AccessScope,
                isTenantMode: session.AccessScope == AccessScopeType.Global && tenant is not null));
    }

    /// <summary>
    /// Re-issues an access token against the SAME session, pointed at a different Organisation.
    /// This is the SuperAdmin switch.
    ///
    /// The session row records the new operating Organisation. Nothing on the USER row changes
    /// — their <c>TenantId</c> stays null, which is the invariant the whole tenancy model rests
    /// on.
    /// </summary>
    public async Task<TokenResponse> ReissueForTenantAsync(
        Guid sessionId, User user, Tenant tenant, BusinessUnit businessUnit,
        string? hostName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = clock.UtcNow;
        var session = await security.GetSessionAsync(sessionId, cancellationToken);

        if (session is null || !session.IsActive(now))
        {
            throw new InvalidOperationException("The session is no longer active.");
        }

        session.OperatingTenantId = tenant.Id;
        session.LastActivityAtUtc = now;

        var access = await effectiveAccess.ResolveAsync(user, tenant.Id, cancellationToken);

        var accessToken = jwtTokenService.CreateAccessToken(
            user, tenant, businessUnit, session.Id, AccessScopeType.Global, access,
            session.ClientType, hostName, session.DeviceIdentifier, session.MfaCompleted);

        // The refresh token is NOT rotated. Switching Organisation is not a new sign-in, and
        // rotating would invalidate the cookie the browser is holding for no reason.
        return new TokenResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            accessToken.ExpiresInSeconds,
            "Bearer",
            string.Empty,
            now.AddDays(_jwt.RefreshTokenDays),
            session.Id,
            BuildUserResponse(user, access),
            BuildTenantResponse(tenant, businessUnit, AccessScopeType.Global, isTenantMode: true));
    }

    public async Task<TokenResponse> ReissueForPlatformAsync(
        Guid sessionId, User user, BusinessUnit businessUnit,
        string? hostName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = clock.UtcNow;
        var session = await security.GetSessionAsync(sessionId, cancellationToken);

        if (session is null || !session.IsActive(now))
        {
            throw new InvalidOperationException("The session is no longer active.");
        }

        // THE POINT OF THE WHOLE METHOD. Clearing this is what makes the exit real: the session
        // row stops naming an Organisation, so the next audit entry no longer claims the caller
        // was standing in one.
        session.OperatingTenantId = null;
        session.LastActivityAtUtc = now;

        // Resolved with no Organisation, so the menu comes back as the platform menu rather than
        // the one belonging to whichever Organisation was open a moment ago.
        var access = await effectiveAccess.ResolveAsync(user, null, cancellationToken);

        var accessToken = jwtTokenService.CreateAccessToken(
            user, null, businessUnit, session.Id, AccessScopeType.Global, access,
            session.ClientType, hostName, session.DeviceIdentifier, session.MfaCompleted);

        // The refresh token is NOT rotated, for the same reason as entering an Organisation:
        // leaving one is not a new sign-in and the browser's cookie is still perfectly good.
        return new TokenResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            accessToken.ExpiresInSeconds,
            "Bearer",
            string.Empty,
            now.AddDays(_jwt.RefreshTokenDays),
            session.Id,
            BuildUserResponse(user, access),
            BuildTenantResponse(null, businessUnit, AccessScopeType.Global, isTenantMode: false));
    }

    public async Task<bool> RevokeAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var session = await security.GetSessionAsync(sessionId, cancellationToken);

        if (session is null || session.RevokedAtUtc is not null)
        {
            return false;
        }

        session.RevokedAtUtc = now;
        session.RevocationReason = reason;

        // The refresh chain goes too. Leaving one alive would let the session be rebuilt
        // seconds after it was revoked.
        await security.RevokeTokenChainAsync(sessionId, reason, now, cancellationToken);

        return true;
    }

    public Task<int> RevokeAllAsync(
        Guid userId, Guid? exceptSessionId, string reason, CancellationToken cancellationToken) =>
        security.RevokeAllSessionsAsync(userId, exceptSessionId, reason, clock.UtcNow, cancellationToken);

    /// <summary>
    /// Moves the idle clock forward.
    ///
    /// Deliberately does NOT save: it is called once per authenticated request, and a
    /// dedicated round trip per request would be a real cost for a field nothing reads
    /// synchronously. The change rides along with whatever the request commits anyway.
    /// </summary>
    public async Task TouchAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await security.GetSessionAsync(sessionId, cancellationToken);

        if (session is not null && session.RevokedAtUtc is null)
        {
            session.LastActivityAtUtc = clock.UtcNow;
        }
    }

    public AuthenticatedUserResponse BuildUserResponse(User user, EffectiveAccess access) =>
        AuthenticationMappingConfig.ToAuthenticatedUser(user, access);

    public TenantContextResponse BuildTenantResponse(
        Tenant? tenant, BusinessUnit businessUnit, AccessScopeType scope, bool isTenantMode) =>
        AuthenticationMappingConfig.ToTenantContext(tenant, businessUnit, scope, isTenantMode);
}
