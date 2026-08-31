using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.PasswordRecovery;

/// <summary>IAM-AUTH-03. Starts a reset.</summary>
public sealed record ForgotPasswordCommand(ForgotPasswordRequest Request);

/// <summary>IAM-AUTH-04. Completes a reset with the token from the e-mail.</summary>
public sealed record ResetPasswordCommand(ResetPasswordRequest Request);

/// <summary>Changes a password from inside a session.</summary>
public sealed record ChangePasswordCommand(ChangePasswordRequest Request);

/// <summary>Confirms an e-mail address from a link.</summary>
public sealed record ConfirmEmailCommand(string Token);

/// <summary>Checks a recovery link before the form is drawn.</summary>
public sealed record GetResetPasswordViewQuery(string Token);

/// <summary>Asks for a fresh recovery link when the current one has lapsed.</summary>
public sealed record RequestNewRecoveryLinkCommand(RequestNewRecoveryLinkRequest Request);

/// <summary>Starts recovery from the account-unavailable screen.</summary>
public sealed record StartRecoveryCommand(StartRecoveryRequest Request);

/// <summary>
/// Password recovery.
///
/// TENANT-SPECIFIC, AND THAT MATTERS MORE HERE THAN ALMOST ANYWHERE. Because users are
/// per-Organisation, a reset requested at ten1.ngoplanet.com must resolve
/// john@gmail.com to the TEN001 user and nobody else. If it resolved globally, anybody who
/// could reach one Organisation sign-in page could send a reset link for the same address in
/// a different Organisation — which is a complete account takeover across a boundary that is
/// supposed to be absolute.
///
/// THE RESPONSE IS ALWAYS THE SAME. Whether or not the address exists, the caller is told a
/// link has been sent. A different message for an unknown address is a free oracle for
/// testing which addresses are registered, and this endpoint is unauthenticated.
/// </summary>
public sealed class PasswordRecoveryCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ISecurityRepository security,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    ISessionTokenService sessions,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    private const string GenericReply =
        "If that account exists, we have sent a password reset link to its e-mail address.";

    /// <summary>
    /// Whether a recovery link is still usable, plus the password rules the new password has to
    /// satisfy.
    ///
    /// CALLED BEFORE THE FORM IS DRAWN. The alternative is somebody carefully choosing a
    /// password, pressing Save, and only then being told the link expired an hour ago.
    ///
    /// IT NAMES NOBODY. A person holding an expired link has proved nothing, so the reply
    /// carries no address and no display name - only whether to show the form, and the rules.
    /// </summary>
    public async Task<Result<ResetPasswordViewResponse>> HandleAsync(
        GetResetPasswordViewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = clock.UtcNow;

        var token = await security.GetRecoveryTokenAsync(
            tokenHasher.Hash(query.Token ?? string.Empty), cancellationToken);

        var valid = token is not null
                    && token.Purpose == RecoveryTokenPurpose.PasswordReset
                    && token.IsRedeemable(now);

        return Result.Success(new ResetPasswordViewResponse(
            IsTokenValid: valid,
            TokenExpiresAtUtc: valid ? token!.ExpiresAtUtc : null,
            _security.PasswordMinimumLength,
            _security.PasswordMaximumLength,
            _security.PasswordRequireUppercase,
            _security.PasswordRequireLowercase,
            _security.PasswordRequireDigit,
            _security.PasswordRequireNonAlphanumeric,
            _security.PasswordHistoryCount,
            SessionRevocationNotice:
                "Changing your password signs you out everywhere else. You will need to sign in "
                + "again on your other devices.",
            Message: valid
                ? "Choose a new password."
                : "That link has expired or has already been used. Ask for a new one."));
    }

    /// <summary>
    /// Sends a fresh recovery link.
    ///
    /// The same generic reply as forgot-password, for the same reason: a reply that differed for
    /// a known address would make this endpoint a way of testing which addresses are registered.
    /// It goes through the same handler so the rate limit, the Organisation scoping and the
    /// token invalidation are the ones already written rather than a second set that drifts.
    /// </summary>
    public Task<Result<ForgotPasswordResponse>> HandleAsync(
        RequestNewRecoveryLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return HandleAsync(
            new ForgotPasswordCommand(new ForgotPasswordRequest(command.Request.Identifier)),
            cancellationToken);
    }

    /// <summary>
    /// Starts recovery from the account-unavailable screen.
    ///
    /// Same path as forgot-password. A suspended account gets a link that lifts the hold and
    /// sets a new password in one step; anybody else gets the ordinary one. Which was sent is
    /// not reported back - see the note on the generic reply.
    /// </summary>
    public Task<Result<ForgotPasswordResponse>> HandleAsync(
        StartRecoveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return HandleAsync(
            new ForgotPasswordCommand(new ForgotPasswordRequest(command.Request.Identifier)),
            cancellationToken);
    }

    public async Task<Result<ForgotPasswordResponse>> HandleAsync(
        ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var identifier = (command.Request.Identifier ?? string.Empty).Trim().ToLowerInvariant();

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<ForgotPasswordResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = tenantContext.TenantId.HasValue
            ? await tenants.GetByIdAsync(tenantContext.TenantId.Value, cancellationToken)
            : null;

        // Scoped to the Organisation the request arrived at. Never a global lookup.
        var user = tenant is null
            ? await users.FindSuperAdminAsync(identifier, cancellationToken)
            : await users.FindForSignInAsync(identifier, tenant.Id, cancellationToken);

        // Unknown address: same reply, no e-mail, and the attempt is still recorded.
        if (user is null || user.Status is UserStatus.Deactivated or UserStatus.Withdrawn)
        {
            await audit.WriteAnonymousAsync(
                AuditActionCodes.PasswordResetRequested, nameof(User), null,
                businessUnit.Id, tenant?.Id, AuditResult.Denied, identifier,
                new { Reason = "No matching account in this organisation." },
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new ForgotPasswordResponse(GenericReply, EmailSent: false));
        }

        // Rate limit per account, so a mailbox cannot be flooded from this endpoint.
        var recent = await security.CountRecentRecoveryRequestsAsync(
            user.Id, RecoveryTokenPurpose.PasswordReset, now.AddHours(-1), cancellationToken);

        if (recent >= _security.PasswordResetRequestsPerHour)
        {
            // Still the generic reply: telling somebody they are rate-limited confirms the
            // account exists just as surely as a "not found" would.
            return Result.Success(new ForgotPasswordResponse(GenericReply, EmailSent: false));
        }

        // Any earlier reset link stops working, so a mailbox full of them has exactly one
        // that does.
        await security.InvalidateRecoveryTokensAsync(
            user.Id, RecoveryTokenPurpose.PasswordReset,
            "Superseded by a newer request.", now, cancellationToken);

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

        await audit.WriteAnonymousAsync(
            AuditActionCodes.PasswordResetRequested, nameof(User), user.Id,
            businessUnit.Id, tenant?.Id, AuditResult.Succeeded, user.DisplayName,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifications.SendPasswordResetAsync(
            user, tenant, businessUnit,
            BuildClientUrl(tenant, businessUnit, _client.ResetPasswordPath, token),
            expiresAt, cancellationToken);

        return Result.Success(new ForgotPasswordResponse(GenericReply, EmailSent: true));
    }

    public async Task<Result<PasswordOperationResponse>> HandleAsync(
        ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.Validation("The passwords do not match.",
                    [new ValidationError(nameof(request.ConfirmPassword), "The passwords do not match.")]));
        }

        var recovery = await security.GetRecoveryTokenAsync(
            tokenHasher.Hash(request.Token ?? string.Empty), cancellationToken);

        if (recovery is null || recovery.Purpose != RecoveryTokenPurpose.PasswordReset)
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenInvalid());
        }

        if (!recovery.IsRedeemable(now))
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenExpired());
        }

        var user = await users.FindByIdInTenantAsync(recovery.UserId, recovery.TenantId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenInvalid());
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);

        var minimumLength = Math.Max(_security.PasswordMinimumLength, tenant?.PasswordMinimumLength ?? 0);
        var failures = passwordHasher.ValidatePolicy(request.Password ?? string.Empty, minimumLength);

        if (failures.Count > 0)
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.WeakPassword("That password does not meet the requirements.",
                    [.. failures.Select(message => new ValidationError(nameof(request.Password), message))]));
        }

        // Reusing the current password is refused: a reset that changes nothing is not a reset.
        if (!string.IsNullOrEmpty(user.PasswordHash)
            && passwordHasher.Verify(user.PasswordHash, request.Password!) != PasswordVerificationOutcome.Failed)
        {
            return Result.Failure<PasswordOperationResponse>(Error.PasswordReused());
        }

        user.PasswordHash = passwordHasher.Hash(request.Password!);
        user.PasswordChangedAtUtc = now;
        user.MustChangePassword = false;

        // A new stamp invalidates every token minted before the reset.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        // A reset also clears a lockout: somebody who has proved control of the mailbox
        // should not still be waiting out a timer caused by whoever was guessing.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.IsLockedOutByAdministrator = false;
        user.LockoutReason = null;

        // An account that had never been activated is now activated by this reset, which is
        // how the reactivation link in the brief works.
        if (user.Status is UserStatus.Invited or UserStatus.Draft)
        {
            user.Status = UserStatus.Active;
            user.EmailConfirmed = true;
            user.EmailConfirmedAtUtc = now;
        }

        recovery.ConsumedAtUtc = now;
        recovery.ConsumedFromIpAddress = currentUser.IpAddress;

        // Every existing session dies. If the reset was somebody recovering a hijacked
        // account, leaving the attacker signed in would defeat the entire exercise.
        await sessions.RevokeAllAsync(
            user.Id, exceptSessionId: null, "Password was reset.", cancellationToken);

        await audit.WriteAnonymousAsync(
            AuditActionCodes.PasswordResetCompleted, nameof(User), user.Id,
            user.BusinessUnitId, user.TenantId, AuditResult.Succeeded, user.DisplayName,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (businessUnit is not null)
        {
            await notifications.SendPasswordChangedAsync(
                user, tenant, businessUnit, currentUser.IpAddress, cancellationToken);
        }

        return Result.Success(new PasswordOperationResponse(
            Succeeded: true,
            "Your password has been changed. Sign in with your new password.",
            RequiresSignIn: true,
            _security.ToPolicyResponse(tenant?.PasswordMinimumLength)));
    }

    public async Task<Result<PasswordOperationResponse>> HandleAsync(
        ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.Validation("The passwords do not match.",
                    [new ValidationError(nameof(request.ConfirmPassword), "The passwords do not match.")]));
        }

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<PasswordOperationResponse>(Error.Unauthorised());
        }

        // The current password is required even inside a session, because a briefly
        // unattended browser must not be enough to take the account over permanently.
        if (string.IsNullOrEmpty(user.PasswordHash)
            || passwordHasher.Verify(user.PasswordHash, request.CurrentPassword ?? string.Empty)
               == PasswordVerificationOutcome.Failed)
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.InvalidCredentials("Your current password is not correct."));
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        var minimumLength = Math.Max(_security.PasswordMinimumLength, tenant?.PasswordMinimumLength ?? 0);
        var failures = passwordHasher.ValidatePolicy(request.NewPassword ?? string.Empty, minimumLength);

        if (failures.Count > 0)
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.WeakPassword("That password does not meet the requirements.",
                    [.. failures.Select(message => new ValidationError(nameof(request.NewPassword), message))]));
        }

        if (passwordHasher.Verify(user.PasswordHash, request.NewPassword!) != PasswordVerificationOutcome.Failed)
        {
            return Result.Failure<PasswordOperationResponse>(
                Error.PasswordReused("Your new password must be different from your current one."));
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword!);
        user.PasswordChangedAtUtc = now;
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        var revoked = 0;
        if (request.SignOutOtherSessions)
        {
            revoked = await sessions.RevokeAllAsync(
                user.Id, currentUser.SessionId, "Password was changed.", cancellationToken);
        }

        await audit.WriteAsync(
            AuditActionCodes.PasswordChanged, nameof(User), user.Id, user.DisplayName,
            new { OtherSessionsRevoked = revoked }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);
        if (businessUnit is not null)
        {
            await notifications.SendPasswordChangedAsync(
                user, tenant, businessUnit, currentUser.IpAddress, cancellationToken);
        }

        return Result.Success(new PasswordOperationResponse(
            Succeeded: true,
            revoked > 0
                ? $"Your password has been changed. {revoked} other session(s) were signed out."
                : "Your password has been changed.",
            RequiresSignIn: false,
            _security.ToPolicyResponse(tenant?.PasswordMinimumLength)));
    }

    /// <summary>
    /// Confirms an e-mail address. Used both for a new account and for the second half of a
    /// login-identifier change, which is why the token carries the address it proves rather
    /// than the handler assuming the current one.
    /// </summary>
    public async Task<Result<PasswordOperationResponse>> HandleAsync(
        ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var recovery = await security.GetRecoveryTokenAsync(
            tokenHasher.Hash(command.Token ?? string.Empty), cancellationToken);

        if (recovery is null || recovery.Purpose != RecoveryTokenPurpose.EmailConfirmation)
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenInvalid());
        }

        if (!recovery.IsRedeemable(now))
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenExpired());
        }

        var user = await users.FindByIdInTenantAsync(recovery.UserId, recovery.TenantId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<PasswordOperationResponse>(Error.TokenInvalid());
        }

        user.EmailConfirmed = true;
        user.EmailConfirmedAtUtc = now;
        recovery.ConsumedAtUtc = now;
        recovery.ConsumedFromIpAddress = currentUser.IpAddress;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.EmailConfirmed, nameof(User), user.Id,
            user.BusinessUnitId, user.TenantId, AuditResult.Succeeded, user.DisplayName,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PasswordOperationResponse(
            Succeeded: true, "Your e-mail address is confirmed.", RequiresSignIn: false));
    }

    /// <summary>
    /// Builds a client link pointed at the Organisation own host, so following it resolves the
    /// right Tenant. A reset link for TEN001 that landed on the platform host would resolve to
    /// no Organisation and fail at the far end.
    /// </summary>
    private string BuildClientUrl(Tenant? tenant, BusinessUnit businessUnit, string path, string token)
    {
        var host = tenant is null ? null : $"{tenant.Subdomain}.{businessUnit.RootDomain}";

        return _client.TenantUrl(host, path, token);
    }
}
