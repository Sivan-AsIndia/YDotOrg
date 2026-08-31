using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Users.Commands.UserLifecycle;

/// <summary>IAM-USR-02. Editing a user profile.</summary>
public sealed record UpdateUserCommand(Guid UserId, UpdateUserRequest Request);

/// <summary>Suspending an account. Sign-in stops; the record and its history remain.</summary>
public sealed record SuspendUserCommand(Guid UserId, UserLifecycleRequest Request);

/// <summary>Lifting a suspension.</summary>
public sealed record ReactivateUserCommand(Guid UserId, ReactivateUserRequest Request);

/// <summary>Deactivating an account. The person has left.</summary>
public sealed record DeactivateUserCommand(Guid UserId, UserLifecycleRequest Request);

/// <summary>Withdrawing an account that was never activated.</summary>
public sealed record WithdrawUserCommand(Guid UserId, UserLifecycleRequest Request);

/// <summary>Clearing a lockout by hand.</summary>
public sealed record UnlockUserCommand(Guid UserId, UnlockUserRequest Request);

/// <summary>An administrator resetting somebody password.</summary>
public sealed record AdminResetPasswordCommand(Guid UserId, AdminResetPasswordRequest Request);

/// <summary>Extending or shortening an access window.</summary>
public sealed record ExtendUserAccessCommand(Guid UserId, ExtendUserAccessRequest Request);

/// <summary>Ending every session a user holds.</summary>
public sealed record ForceUserSignOutCommand(Guid UserId, string Reason);

/// <summary>
/// The user lifecycle.
///
/// THE STATUSES ARE NOT INTERCHANGEABLE, and the distinctions matter operationally:
///
/// <code>
/// Suspended    temporary, reversible. "You are locked out while we look into this."
/// Deactivated  the person has left. Reversible only by an administrator, deliberately.
/// Withdrawn    invited, never activated, no longer joining. Never had credentials.
/// Expired      the access window closed on its own. Nobody did anything.
/// </code>
///
/// Collapsing these into an IsActive boolean would throw away exactly the information the
/// person on the other end of the support call needs.
///
/// SUSPENDING KILLS SESSIONS IMMEDIATELY. A status change alone would leave anybody already
/// signed in working for up to fifteen minutes on a still-valid access token, which is not
/// what "suspend this account now" means to whoever asked for it.
/// </summary>
public sealed class UserLifecycleCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ISecurityRepository security,
    ISessionTokenService sessions,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    INotificationService notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    // =================================================================================
    // Edit
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // A manager cannot be their own manager. The database enforces it too, but a check
        // constraint violation is a 500 and this is a 400 with a field message.
        if (request.ManagerUserId.HasValue && request.ManagerUserId.Value == user.Id)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("A user cannot be their own manager.",
                    [new ValidationError(nameof(request.ManagerUserId), "Choose a different manager.")]));
        }

        if (request.ManagerUserId.HasValue && request.ManagerUserId != user.ManagerUserId)
        {
            var manager = await users.GetByIdAsync(request.ManagerUserId.Value, cancellationToken);
            if (manager is null)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Validation("That manager was not found in this organisation.",
                        [new ValidationError(nameof(request.ManagerUserId), "Choose a manager from this organisation.")]));
            }
        }

        // Mobile is validated as a pair, because a number without its country code is not a
        // number anybody can dial.
        if (!string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            var mobile = MobileNumberValue.TryParse(
                request.MobileCountryCode ?? user.MobileCountryCode, request.MobileNumber);

            if (mobile is null)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Validation("Enter a valid mobile number with its country code.",
                        [new ValidationError(nameof(request.MobileNumber), "That mobile number is not valid.")]));
            }
        }

        var changes = request.ApplyTo(user);

        // A profile edit does not change credentials, so the security stamp is deliberately
        // left alone: invalidating live tokens because somebody fixed a job title would be a
        // surprising and unhelpful side effect.
        // The changed fields AND the stated reason. The trail can always show what moved; only
        // the person making the change can say why, and "corrected a typo" versus "changed at
        // the person's request" look identical in a diff.
        await audit.WriteAsync(
            AuditActionCodes.UserUpdated, nameof(User), user.Id, user.DisplayName,
            new { ChangedFields = changes, request.Reason },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            "User saved.", UserMappingConfig.PermittedActionsFor(user, clock.UtcNow)));
    }

    // =================================================================================
    // Status transitions
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SuspendUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await TransitionAsync(
            command.UserId, UserStatus.Suspended, command.Request.ExpectedVersion,
            command.Request.Reason, AuditActionCodes.UserSuspended,
            // Suspension has to bite now, not when the access token happens to expire.
            revokeSessions: true,
            "Account suspended. Their sessions have been ended.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await TransitionAsync(
            command.UserId, UserStatus.Deactivated, command.Request.ExpectedVersion,
            command.Request.Reason, AuditActionCodes.UserDeactivated,
            revokeSessions: true,
            "Account deactivated.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        WithdrawUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await TransitionAsync(
            command.UserId, UserStatus.Withdrawn, command.Request.ExpectedVersion,
            command.Request.Reason, AuditActionCodes.UserCancelled,
            revokeSessions: true,
            "Account withdrawn.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (user.Status is not (UserStatus.Suspended or UserStatus.Deactivated or UserStatus.Expired))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"An account that is {user.Status} cannot be reactivated."));
        }

        // An account with no password has never been activated, so "reactivate" would leave
        // somebody Active and still unable to sign in. It goes back to Invited instead.
        user.Status = string.IsNullOrEmpty(user.PasswordHash) ? UserStatus.Invited : UserStatus.Active;

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.IsLockedOutByAdministrator = false;
        user.LockoutReason = null;

        // If the access window had closed, push it out so reactivation actually takes effect
        // rather than the person bouncing straight back to Expired.
        var now = clock.UtcNow;
        if (user.AccessEndsAtUtc.HasValue && user.AccessEndsAtUtc.Value <= now)
        {
            user.AccessEndsAtUtc = null;
        }

        await audit.WriteAsync(
            AuditActionCodes.UserReactivated, nameof(User), user.Id, user.DisplayName,
            new { command.Request.Notes }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            user.Status == UserStatus.Invited
                ? "Account reactivated. They still need to accept their invitation."
                : "Account reactivated.",
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    // =================================================================================
    // Credentials
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UnlockUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.IsLockedOutByAdministrator = false;
        user.LockoutReason = null;

        await audit.WriteAsync(
            AuditActionCodes.UserUnlocked, nameof(User), user.Id, user.DisplayName,
            new { command.Request.Reason }, command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            "Account unlocked.", UserMappingConfig.PermittedActionsFor(user, clock.UtcNow)));
    }

    /// <summary>
    /// An administrator resetting a password.
    ///
    /// The link is strongly preferred and is the default. A temporary password has to be
    /// communicated out of band, and in practice it gets sent over the same channel it was
    /// meant to protect — so when one is issued it is returned ONCE, in this response, and
    /// never stored or e-mailed.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        AdminResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Dependency("The platform is not configured."));
        }

        string message;
        string? issuedPassword = null;

        if (request.SendResetLink)
        {
            await security.InvalidateRecoveryTokensAsync(
                user.Id, RecoveryTokenPurpose.PasswordReset,
                "Superseded by an administrator reset.", now, cancellationToken);

            var token = tokenHasher.GenerateToken();
            var expiresAt = now.AddMinutes(_security.PasswordResetExpiryMinutes);

            await security.AddRecoveryTokenAsync(new RecoveryToken
            {
                TenantId = user.TenantId ?? Guid.Empty,
                BusinessUnitId = user.BusinessUnitId,
                UserId = user.Id,
                Purpose = RecoveryTokenPurpose.PasswordReset,
                TokenHash = tokenHasher.Hash(token),
                IssuedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                RequestedFromIpAddress = currentUser.IpAddress,
                RequestedUserAgent = currentUser.UserAgent
            }, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var resetUrl = BuildResetUrl(tenant, businessUnit, token);

            await notifications.SendPasswordResetAsync(
                user, tenant, businessUnit, resetUrl, expiresAt, cancellationToken);

            message = "A password reset link has been e-mailed to them.";
        }
        else
        {
            issuedPassword = string.IsNullOrWhiteSpace(request.TemporaryPassword)
                ? passwordHasher.GenerateTemporaryPassword()
                : request.TemporaryPassword;

            var minimumLength = Math.Max(_security.PasswordMinimumLength, tenant?.PasswordMinimumLength ?? 0);
            var failures = passwordHasher.ValidatePolicy(issuedPassword, minimumLength);

            if (failures.Count > 0)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.WeakPassword("That temporary password does not meet the requirements.",
                        [.. failures.Select(text => new ValidationError(nameof(request.TemporaryPassword), text))]));
            }

            user.PasswordHash = passwordHasher.Hash(issuedPassword);
            user.PasswordChangedAtUtc = now;
            user.MustChangePassword = request.RequireChangeOnNextSignIn;
            user.CredentialSetupMethod = CredentialSetupMethod.AdministratorSet;

            // A credential change invalidates every existing token immediately.
            user.SecurityStamp = Guid.NewGuid().ToString("N");

            if (user.Status is UserStatus.Invited or UserStatus.Draft)
            {
                user.Status = UserStatus.Active;
            }

            message = "A temporary password has been set. Give it to them directly - it is shown only once.";
        }

        if (request.SignOutAllSessions)
        {
            await sessions.RevokeAllAsync(
                user.Id, exceptSessionId: null, "Password was reset by an administrator.", cancellationToken);
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.IsLockedOutByAdministrator = false;

        await audit.WriteAsync(
            AuditActionCodes.UserPasswordResetByAdmin, nameof(User), user.Id, user.DisplayName,
            new { request.SendResetLink, request.SignOutAllSessions, request.RequireChangeOnNextSignIn },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The plaintext travels back exactly once, in the response the administrator is
        // already looking at. It is never persisted and never e-mailed.
        TemporaryPasswordAccessor.Set(issuedPassword);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version, message,
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ExtendUserAccessCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (command.Request.AccessEndsAtUtc.HasValue
            && command.Request.AccessEndsAtUtc.Value <= user.AccessStartsAtUtc)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("The end date must be after the start date.",
                    [new ValidationError(nameof(command.Request.AccessEndsAtUtc),
                        "Choose a date after the access start date.")]));
        }

        user.AccessEndsAtUtc = command.Request.AccessEndsAtUtc;

        // Extending an expired account brings it back, which is what somebody extending it
        // plainly intends.
        if (user.Status == UserStatus.Expired && !user.IsOutsideAccessWindow(now))
        {
            user.Status = string.IsNullOrEmpty(user.PasswordHash) ? UserStatus.Invited : UserStatus.Active;
        }

        await audit.WriteAsync(
            AuditActionCodes.UserUpdated, nameof(User), user.Id, user.DisplayName,
            new { AccessEndsAtUtc = command.Request.AccessEndsAtUtc, command.Request.Reason },
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            command.Request.AccessEndsAtUtc.HasValue
                ? "Access period updated."
                : "Access period removed. This account no longer expires.",
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ForceUserSignOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        var revoked = await sessions.RevokeAllAsync(
            user.Id, exceptSessionId: null, command.Reason, cancellationToken);

        // The stamp changes too, so an access token already in flight is refused rather than
        // working until it expires.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await audit.WriteAsync(
            AuditActionCodes.SignOutEverywhere, nameof(User), user.Id, user.DisplayName,
            new { SessionsRevoked = revoked, command.Reason }, command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            $"Ended {revoked} session(s).", UserMappingConfig.PermittedActionsFor(user, clock.UtcNow)));
    }

    // =================================================================================
    // Shared transition
    // =================================================================================

    /// <summary>
    /// The one path a status change takes: version check, legality check, session revocation
    /// where the status demands it, audit row.
    /// </summary>
    private async Task<Result<OutcomeResponse>> TransitionAsync(
        Guid userId,
        UserStatus target,
        long expectedVersion,
        string reason,
        string actionCode,
        bool revokeSessions,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        if (user.Version != expectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // A system account cannot be disabled through the UI. Suspending the seeded
        // administrator is how an Organisation locks itself out permanently.
        if (user.IsSystemAccount)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("System accounts cannot be changed."));
        }

        // Nobody may suspend themselves. It is always an accident, and the person who did it
        // is then the one person who cannot undo it.
        if (user.Id == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("You cannot change the status of your own account."));
        }

        if (!UserMappingConfig.CanTransitionTo(user.Status, target))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"An account that is {user.Status} cannot be moved to {target}."));
        }

        user.Status = target;

        if (target == UserStatus.Suspended)
        {
            user.LockoutReason = reason;
        }

        if (target == UserStatus.Deactivated || target == UserStatus.Withdrawn)
        {
            user.ExitedOn ??= now;
        }

        if (revokeSessions)
        {
            await sessions.RevokeAllAsync(user.Id, exceptSessionId: null, reason, cancellationToken);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        await audit.WriteAsync(
            actionCode, nameof(User), user.Id, user.DisplayName,
            new { NewStatus = target.ToString(), Reason = reason }, reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version, successMessage,
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    private string BuildResetUrl(Tenant? tenant, BusinessUnit businessUnit, string token)
    {
        var host = tenant is null ? null : $"{tenant.Subdomain}.{businessUnit.RootDomain}";

        return _client.TenantUrl(host, _client.ResetPasswordPath, token);
    }
}

/// <summary>
/// Carries a freshly issued temporary password from the handler out to the API layer.
///
/// Same reasoning as the trusted-device token: it is a secret produced by ONE branch of a
/// flow whose response type is shared, and threading it through <c>OutcomeResponse</c> would
/// put a live credential in a DTO that every other lifecycle action also returns. The API
/// layer reads it once and attaches it to that single response.
/// </summary>
public static class TemporaryPasswordAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public static void Set(string? password) => Current.Value = password;

    /// <summary>Reads and clears, so it cannot leak into a later request on the same thread.</summary>
    public static string? Take()
    {
        var value = Current.Value;
        Current.Value = null;
        return value;
    }
}
