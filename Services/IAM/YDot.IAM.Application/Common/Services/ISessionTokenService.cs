using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Services;

/// <summary>
/// Opens, rotates and closes sessions, and mints the tokens that belong to them.
///
/// WHY THIS EXISTS RATHER THAN EACH HANDLER CALLING IJwtTokenService. Issuing a working
/// session is six coordinated steps: resolve effective access, create the session row, mint
/// the access token against it, mint and hash a refresh token, trim the user oldest sessions
/// if they are over the ceiling, and stamp the last-login capture on the user. Sign-in, MFA
/// verification, invitation acceptance, refresh and Organisation switching ALL need exactly
/// that sequence. Six copies of it would drift, and the one that drifted would be the one
/// that forgot to record the session — which is the one that cannot then be revoked.
/// </summary>
public interface ISessionTokenService
{
    /// <summary>
    /// Opens a session and issues the token pair.
    ///
    /// <paramref name="operatingTenant"/> is the Organisation the session works inside. For a
    /// Tenant user it is their own. For SuperAdmin it is whichever they selected, or null
    /// before they have chosen — and their <c>User.TenantId</c> is untouched either way.
    /// </summary>
    Task<TokenResponse> IssueAsync(
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
        CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the old one.
    ///
    /// REUSE IS TREATED AS THEFT. Presenting a token that has already been consumed means two
    /// parties hold it, so the whole rotation chain and its session are destroyed rather than
    /// the one request being refused. That is why the Angular interceptor funnels parallel
    /// 401s through a single shared refresh — six simultaneous refreshes look exactly like an
    /// attack.
    /// </summary>
    Task<TokenResponse?> RefreshAsync(
        string refreshToken, string? hostName, CancellationToken cancellationToken);

    /// <summary>
    /// Re-issues an access token against the SAME session, pointed at a different
    /// Organisation. This is SuperAdmin switching.
    ///
    /// The session row records the new operating Organisation, so the audit trail can say
    /// which Organisation a root user was standing in. Nothing on the user row changes.
    /// </summary>
    Task<TokenResponse> ReissueForTenantAsync(
        Guid sessionId, User user, Tenant tenant, BusinessUnit businessUnit,
        string? hostName, CancellationToken cancellationToken);

    /// <summary>
    /// Re-issues an access token against the SAME session with NO operating Organisation, taking
    /// a root user back to platform scope. The counterpart to
    /// <see cref="ReissueForTenantAsync"/>.
    ///
    /// WHY IT HAS TO EXIST. Entering an Organisation was a one-way door: the only way back out was
    /// to sign out and in again. That is worse than an inconvenience, because the token keeps
    /// naming that Organisation, and tenant_id is what stamps every write, filters every query and
    /// labels every audit row. A root user who has finished inside one Organisation should stop
    /// carrying it, rather than carrying it silently into whatever they do next.
    /// </summary>
    Task<TokenResponse> ReissueForPlatformAsync(
        Guid sessionId, User user, BusinessUnit businessUnit,
        string? hostName, CancellationToken cancellationToken);

    /// <summary>Ends one session and revokes its whole token chain.</summary>
    Task<bool> RevokeAsync(Guid sessionId, string reason, CancellationToken cancellationToken);

    /// <summary>Ends every session for a user, optionally sparing the current one.</summary>
    Task<int> RevokeAllAsync(
        Guid userId, Guid? exceptSessionId, string reason, CancellationToken cancellationToken);

    /// <summary>Moves the idle clock forward. Called once per authenticated request.</summary>
    Task TouchAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Builds the compact user block embedded in every auth response.</summary>
    AuthenticatedUserResponse BuildUserResponse(User user, Models.EffectiveAccess access);

    /// <summary>Builds the Organisation context block embedded in every auth response.</summary>
    TenantContextResponse BuildTenantResponse(
        Tenant? tenant, BusinessUnit businessUnit, AccessScopeType scope, bool isTenantMode);
}
