using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.Tokens;

/// <summary>Exchanges a refresh token for a new pair.</summary>
public sealed record RefreshTokenCommand(RefreshTokenRequest Request);

/// <summary>Ends the current session, or every session for the caller.</summary>
public sealed record SignOutCommand(SignOutRequest Request);

/// <summary>Ends one named session from the security screen.</summary>
public sealed record RevokeSessionCommand(RevokeSessionRequest Request);

/// <summary>
/// The token lifecycle.
///
/// HOW REVOCATION ACTUALLY WORKS HERE, because it is the part that surprises people. A JWT
/// cannot be un-issued — it is valid until it expires, wherever it happens to be. So the
/// design gives it two leashes:
///
///   1. A SHORT LIFETIME. Fifteen minutes, so a stolen access token is worth very little.
///   2. A SESSION ROW plus a SECURITY STAMP baked into the token. Revoking the session, or
///      changing the stamp, means the next request carrying that token is refused even though
///      its signature and expiry are both still perfectly valid.
///
/// That pairing is what makes "sign out on my lost phone" immediate rather than a
/// fifteen-minute promise.
/// </summary>
public sealed class TokenCommandHandler(
    ISessionTokenService sessions,
    ISecurityRepository security,
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IEffectiveAccessService effectiveAccess,
    IAuditService audit,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<TokenResponse>> HandleAsync(
        RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var presented = command.Request.RefreshToken;

        if (string.IsNullOrWhiteSpace(presented))
        {
            // Normally the token arrives in the HttpOnly cookie and the API layer copies it
            // onto the request before we get here. Nothing to work with means the cookie was
            // never set, or has expired.
            return Result.Failure<TokenResponse>(Error.SessionExpired());
        }

        var refreshed = await sessions.RefreshAsync(presented, tenantContext.HostName, cancellationToken);

        if (refreshed is null)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<TokenResponse>(Error.SessionExpired());
        }

        await audit.WriteAsync(
            AuditActionCodes.TokenRefreshed, nameof(UserSession), refreshed.SessionId,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(refreshed);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SignOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.SessionId is null)
        {
            // Already signed out. Reported as success: the caller asked for a state that now
            // holds, and answering 401 to a sign-out is a confusing way to say "fine".
            return Result.Success(new OutcomeResponse(
                Guid.Empty, "SignedOut", 0, "You are signed out.", []));
        }

        if (command.Request.AllDevices)
        {
            var revoked = await sessions.RevokeAllAsync(
                currentUser.UserId, exceptSessionId: null, "Signed out of all devices.", cancellationToken);

            await audit.WriteAsync(
                AuditActionCodes.SignOutEverywhere, nameof(User), currentUser.UserId,
                currentUser.DisplayName, new { SessionsRevoked = revoked },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new OutcomeResponse(
                currentUser.UserId, "SignedOut", 0,
                $"Signed out of {revoked} session(s).", []));
        }

        await sessions.RevokeAsync(currentUser.SessionId.Value, "Signed out.", cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.SignOut, nameof(UserSession), currentUser.SessionId,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            currentUser.SessionId.Value, "SignedOut", 0, "You are signed out.", []));
    }

    /// <summary>
    /// Ends one specific session, from the "your devices" screen.
    ///
    /// A person may always revoke their own sessions. Revoking somebody else needs the
    /// user-security permission, which the endpoint enforces — but it is re-checked here,
    /// because a route attribute protects a route and this method is the thing that actually
    /// destroys the session.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await security.GetSessionAsync(command.Request.SessionId, cancellationToken);

        if (session is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That session was not found."));
        }

        var isOwnSession = session.UserId == currentUser.UserId;

        if (!isOwnSession && !currentUser.HasPermission(PermissionCodes.UserSecurityRevokeSession))
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden());
        }

        var reason = string.IsNullOrWhiteSpace(command.Request.Reason)
            ? (isOwnSession ? "Revoked by the account owner." : "Revoked by an administrator.")
            : command.Request.Reason;

        await sessions.RevokeAsync(session.Id, reason, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.SessionRevoked, nameof(UserSession), session.Id,
            session.DeviceName, new { session.UserId, Reason = reason },
            reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            session.Id, "Revoked", session.Version, "That session has been ended.", []));
    }

    /// <summary>
    /// The current session as the idle banner needs it, so the client counts down against the
    /// server clock rather than its own — which drifts, and which the person can change.
    /// </summary>
    public async Task<Result<SessionStatusResponse>> GetSessionStatusAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.SessionId is null)
        {
            return Result.Success(new SessionStatusResponse(
                false, null, null, null, null, 0, 0, false, true, null, null));
        }

        var session = await security.GetSessionAsync(currentUser.SessionId.Value, cancellationToken);
        var now = clock.UtcNow;

        if (session is null || !session.IsActive(now))
        {
            return Result.Success(new SessionStatusResponse(
                false, currentUser.SessionId, null, null, null, 0, 0, false, true, null, null));
        }

        var tenant = session.OperatingTenantId.HasValue
            ? await tenants.GetByIdAsync(session.OperatingTenantId.Value, cancellationToken)
            : null;

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        var idleMinutes = tenant?.SessionIdleTimeoutMinutes ?? 30;
        var idleDeadline = session.LastActivityAtUtc.AddMinutes(idleMinutes);
        var secondsRemaining = (int)Math.Max(0, (idleDeadline - now).TotalSeconds);

        var user = await users.GetWithAccessAsync(currentUser.UserId, cancellationToken);

        AuthenticatedUserResponse? userResponse = null;
        if (user is not null)
        {
            var access = await effectiveAccess.ResolveAsync(user, tenant?.Id, cancellationToken);
            userResponse = sessions.BuildUserResponse(user, access);
        }

        return Result.Success(new SessionStatusResponse(
            IsAuthenticated: true,
            session.Id,
            session.IssuedAtUtc,
            session.ExpiresAtUtc,
            session.LastActivityAtUtc,
            idleMinutes,
            secondsRemaining,
            session.MfaCompleted,
            RequiresReauthentication: secondsRemaining <= 0,
            userResponse,
            businessUnit is null
                ? null
                : sessions.BuildTenantResponse(
                    tenant, businessUnit, session.AccessScope,
                    isTenantMode: session.AccessScope == AccessScopeType.Global && tenant is not null)));
    }
}
