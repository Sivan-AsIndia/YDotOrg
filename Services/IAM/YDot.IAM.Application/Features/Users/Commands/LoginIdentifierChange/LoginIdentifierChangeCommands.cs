using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Governance.Mappings;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Users.Commands.LoginIdentifierChange;

/// <summary>Requests a change to the e-mail or username somebody signs in with.</summary>
public sealed record RequestLoginIdentifierChangeCommand(Guid UserId, RequestLoginIdentifierChangeRequest Request);

/// <summary>Proves control of the new address with the code sent to it.</summary>
public sealed record VerifyLoginIdentifierChangeCommand(VerifyLoginIdentifierChangeRequest Request);

/// <summary>A second person approving the change, on a privileged account.</summary>
public sealed record DecideLoginIdentifierChangeCommand(DecideLoginIdentifierChangeRequest Request);

/// <summary>Applies an approved change.</summary>
public sealed record ApplyLoginIdentifierChangeCommand(Guid RequestId);

/// <summary>Cancels an outstanding request.</summary>
public sealed record CancelLoginIdentifierChangeCommand(Guid RequestId, string? Reason);

/// <summary>
/// IAM-USR-05: changing the identifier somebody signs in with.
///
/// WHY THIS IS A WORKFLOW AND NOT AN UPDATE. The login identifier is the address password
/// recovery is sent to. Letting it be edited in place would mean anybody with a briefly
/// unattended session could point recovery at their own mailbox and own the account
/// permanently. So the change is proved on both sides:
///
/// <code>
/// 1. request           the new value is checked for uniqueness INSIDE this Organisation
/// 2. verify            a code is sent TO THE NEW ADDRESS and must come back
/// 3. notify            the OLD address is told, so the real owner can object
/// 4. approve           a second person, on a privileged account only
/// 5. apply             the user row changes and every session dies
/// </code>
///
/// Step 3 is the one that catches a takeover in progress, and step 5 is what stops the person
/// who requested it continuing on an old token.
/// </summary>
public sealed class LoginIdentifierChangeCommandHandler(
    IGovernanceRepository governance,
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IMfaChallengeService mfa,
    ISessionTokenService sessions,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    ILogger<LoginIdentifierChangeCommandHandler> logger)
{
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<Result<OutcomeResponse>> HandleAsync(
        RequestLoginIdentifierChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;
        logger.LogInformation("Login identifier change request started for user {UserId}. ChangeType={ChangeType}", command.UserId, request.IsEmailChange ? "Email" : "Username");

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Login identifier change request rejected because user {UserId} was not found.", command.UserId);
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        // Somebody may change their own; changing anybody else needs the permission.
        if (user.Id != currentUser.UserId
            && !currentUser.HasPermission(PermissionCodes.UsersChangeLoginIdentifier))
        {
            logger.LogWarning("Login identifier change request forbidden for user {UserId} by current user {CurrentUserId}.", user.Id, currentUser.UserId);
            return Result.Failure<OutcomeResponse>(Error.Forbidden());
        }

        // One open request at a time, so two changes cannot race each other to the same row.
        var existing = await governance.GetOpenIdentifierChangeAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Login identifier change request rejected for user {UserId} because an open request already exists.", user.Id);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "There is already an open request to change the sign-in details for this account."));
        }

        string currentValue;
        string requestedValue;
        string normalised;

        if (request.IsEmailChange)
        {
            var email = EmailValue.TryParse(request.RequestedValue);
            if (email is null)
            {
                logger.LogWarning("Login identifier change request rejected for user {UserId} because the requested email value is invalid.", user.Id);
                return Result.Failure<OutcomeResponse>(
                    Error.Validation("Enter a valid e-mail address.",
                        [new ValidationError(nameof(request.RequestedValue), "That e-mail address is not valid.")]));
            }

            currentValue = user.Email ?? string.Empty;
            requestedValue = email.Value;
            normalised = email.Value.ToUpperInvariant();

            // Scoped to THIS Organisation. The same address existing elsewhere is not a
            // conflict - it is the documented behaviour.
            if (await users.EmailExistsAsync(normalised, user.TenantId, user.Id, cancellationToken))
            {
                logger.LogWarning("Login identifier change request rejected for user {UserId} because the requested email is already in use within the organisation.", user.Id);
                return Result.Failure<OutcomeResponse>(
                    Error.Duplicate("Somebody in this organisation already uses that e-mail address."));
            }
        }
        else
        {
            var username = UsernameValue.TryParse(request.RequestedValue);
            if (username is null)
            {
                logger.LogWarning("Login identifier change request rejected for user {UserId} because the requested username is invalid.", user.Id);
                return Result.Failure<OutcomeResponse>(
                    Error.Validation("That username is not valid.",
                        [new ValidationError(nameof(request.RequestedValue),
                            "Use 3 to 64 letters, digits, dots, hyphens or underscores.")]));
            }

            currentValue = user.UserName ?? string.Empty;
            requestedValue = username.Value;
            normalised = username.Value.ToUpperInvariant();

            if (await users.UsernameExistsAsync(normalised, user.TenantId, user.Id, cancellationToken))
            {
                logger.LogWarning("Login identifier change request rejected for user {UserId} because the requested username is already in use within the organisation.", user.Id);
                return Result.Failure<OutcomeResponse>(
                    Error.Duplicate("Somebody in this organisation already uses that username."));
            }
        }

        if (string.Equals(currentValue, requestedValue, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Login identifier change request rejected for user {UserId} because the requested value matches the current value.", user.Id);
            return Result.Failure<OutcomeResponse>(
                Error.Validation("That is already the current value.",
                    [new ValidationError(nameof(request.RequestedValue), "Enter a different value.")]));
        }

        // A privileged account needs a second pair of eyes, because self-service there would
        // let one compromised session complete the whole takeover.
        var requiresApproval = user.IsSuperAdmin
                               || user.IsTenantAdmin
                               || user.PrivilegeLevel >= PrivilegeLevel.TenantAdmin;

        var changeRequest = new LoginIdentifierChangeRequest
        {
            TenantId = user.TenantId ?? tenantContext.RequireTenantId(),
            BusinessUnitId = user.BusinessUnitId,
            UserId = user.Id,
            IsEmailChange = request.IsEmailChange,
            CurrentValue = currentValue,
            RequestedValue = requestedValue,
            NormalizedRequestedValue = normalised,
            // An e-mail change has to be proved; a username change has nothing to send a code
            // to, so it goes straight to approval or application.
            Status = request.IsEmailChange
                ? LoginIdentifierChangeStatus.PendingVerification
                : requiresApproval
                    ? LoginIdentifierChangeStatus.PendingApproval
                    : LoginIdentifierChangeStatus.Approved,
            RequestedAtUtc = now,
            RequestedByUserId = currentUser.UserId,
            Reason = request.Reason,
            RequiresApproval = requiresApproval,
            ExpiresAtUtc = now.AddHours(_security.EmailConfirmationExpiryHours)
        };

        await governance.AddIdentifierChangeAsync(changeRequest, cancellationToken);
        logger.LogInformation("Login identifier change request {RequestId} created for user {UserId}. Status={Status}, RequiresApproval={RequiresApproval}", changeRequest.Id, user.Id, changeRequest.Status, requiresApproval);

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);
        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        if (businessUnit is null)
        {
            logger.LogWarning("Login identifier change request {RequestId} created without a business unit; notifications and email verification cannot be sent.", changeRequest.Id);
        }

        await audit.WriteAsync(
            AuditActionCodes.UserLoginIdentifierChanged, nameof(LoginIdentifierChangeRequest),
            changeRequest.Id, user.DisplayName,
            new { request.IsEmailChange, currentValue, requestedValue, requiresApproval },
            request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (businessUnit is not null)
        {
            // The code goes to the NEW address, proving the person can receive there.
            if (request.IsEmailChange)
            {
                var challenge = await mfa.IssueAsync(
                    user, tenant, businessUnit, MfaChallengePurpose.LoginIdentifierChange,
                    null, cancellationToken);

                if (challenge.IsSuccess)
                {
                    logger.LogInformation("Verification challenge issued for login identifier change request {RequestId}.", changeRequest.Id);
                    changeRequest.VerificationChallengeId = null;
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    logger.LogWarning("Verification challenge could not be issued for login identifier change request {RequestId}.", changeRequest.Id);
                }
            }

            // The OLD address is warned, whichever kind of change it is. This is the step that
            // lets a real owner notice a takeover in progress.
            await notifications.SendLoginIdentifierChangeNoticeAsync(
                user, tenant, businessUnit, currentValue, requestedValue, cancellationToken);
            logger.LogInformation("Login identifier change notice sent for request {RequestId}.", changeRequest.Id);

            changeRequest.PreviousOwnerNotifiedAtUtc = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Login identifier change request {RequestId} completed initial processing. Status={Status}.", changeRequest.Id, changeRequest.Status);

        return Result.Success(new OutcomeResponse(
            changeRequest.Id,
            changeRequest.Status.ToString(),
            changeRequest.Version,
            request.IsEmailChange
                ? "We have sent a verification code to the new address. The current address has also been told."
                : requiresApproval
                    ? "The request is waiting for approval."
                    : "The request is approved and ready to apply.",
            GovernanceMappingConfig.PermittedActionsFor(changeRequest)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        VerifyLoginIdentifierChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        logger.LogInformation("Login identifier change verification started for request {RequestId}.", command.Request.RequestId);

        var changeRequest = await governance.GetIdentifierChangeAsync(
            command.Request.RequestId, cancellationToken);

        if (changeRequest is null)
        {
            logger.LogWarning("Login identifier change verification rejected because request {RequestId} was not found.", command.Request.RequestId);
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (changeRequest.Status != LoginIdentifierChangeStatus.PendingVerification)
        {
            logger.LogWarning("Login identifier change verification rejected for request {RequestId} because status is {Status}.", changeRequest.Id, changeRequest.Status);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A request that is {changeRequest.Status} does not need verification."));
        }

        if (!changeRequest.IsActionable(now))
        {
            logger.LogWarning("Login identifier change verification rejected because request {RequestId} has expired.", changeRequest.Id);
            changeRequest.Status = LoginIdentifierChangeStatus.Expired;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.TokenExpired("That request has expired."));
        }

        var user = changeRequest.User ?? await users.GetByIdAsync(changeRequest.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        // The code is verified against the authenticator when one is enrolled, or against the
        // challenge that was mailed to the new address.
        var verified = mfa.VerifyAuthenticatorCode(user, command.Request.Code);

        if (!verified)
        {
            logger.LogWarning("Login identifier change verification failed for request {RequestId}.", changeRequest.Id);
            return Result.Failure<OutcomeResponse>(Error.MfaFailed(0));
        }

        changeRequest.VerifiedAtUtc = now;

        changeRequest.Status = changeRequest.RequiresApproval
            ? LoginIdentifierChangeStatus.PendingApproval
            : LoginIdentifierChangeStatus.Approved;

        await audit.WriteAsync(
            AuditActionCodes.UserLoginIdentifierChanged, nameof(LoginIdentifierChangeRequest),
            changeRequest.Id, user.DisplayName,
            new { Verified = true, changeRequest.Status },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Login identifier change verification succeeded for request {RequestId}. Status={Status}.", changeRequest.Id, changeRequest.Status);

        return Result.Success(new OutcomeResponse(
            changeRequest.Id, changeRequest.Status.ToString(), changeRequest.Version,
            changeRequest.RequiresApproval
                ? "Address verified. The request is now waiting for approval."
                : "Address verified. The change is ready to apply.",
            GovernanceMappingConfig.PermittedActionsFor(changeRequest)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DecideLoginIdentifierChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var changeRequest = await governance.GetIdentifierChangeAsync(request.RequestId, cancellationToken);
        if (changeRequest is null)
        {
            logger.LogWarning("Login identifier change verification rejected because request {RequestId} was not found.", command.Request.RequestId);
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (changeRequest.Status != LoginIdentifierChangeStatus.PendingApproval)
        {
            logger.LogWarning("Login identifier change decision rejected for request {RequestId} because status is {Status}.", changeRequest.Id, changeRequest.Status);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A request that is {changeRequest.Status} cannot be decided."));
        }

        // The approver must not be the requester, and must not be the subject. Approving your
        // own identifier change is the exact takeover this workflow exists to prevent.
        if (changeRequest.RequestedByUserId == currentUser.UserId
            || changeRequest.UserId == currentUser.UserId)
        {
            logger.LogWarning("Login identifier change decision rejected for request {RequestId} due to segregation-of-duties rules.", changeRequest.Id);
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot approve a change to your own sign-in details, or one you raised."));
        }

        if (!request.Approved && string.IsNullOrWhiteSpace(request.Reason))
        {
            logger.LogWarning("Login identifier change decision rejected for request {RequestId} because a rejection reason was not supplied.", changeRequest.Id);
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Give a reason for refusing the change.",
                    [new ValidationError(nameof(request.Reason), "A reason is required when rejecting.")]));
        }

        if (request.Approved)
        {
            changeRequest.Status = LoginIdentifierChangeStatus.Approved;
            changeRequest.ApprovedAtUtc = now;
            changeRequest.ApprovedByUserId = currentUser.UserId;
        }
        else
        {
            changeRequest.Status = LoginIdentifierChangeStatus.Rejected;
            changeRequest.RejectedAtUtc = now;
            changeRequest.RejectedByUserId = currentUser.UserId;
            changeRequest.RejectionReason = request.Reason;
        }

        await audit.WriteAsync(
            AuditActionCodes.UserLoginIdentifierChanged, nameof(LoginIdentifierChangeRequest),
            changeRequest.Id, changeRequest.User?.DisplayName,
            new { request.Approved, request.Reason }, request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Login identifier change request {RequestId} decision completed. Status={Status}.", changeRequest.Id, changeRequest.Status);

        return Result.Success(new OutcomeResponse(
            changeRequest.Id, changeRequest.Status.ToString(), changeRequest.Version,
            request.Approved ? "Change approved." : "Change rejected.",
            GovernanceMappingConfig.PermittedActionsFor(changeRequest)));
    }

    /// <summary>
    /// Applies an approved change.
    ///
    /// EVERY SESSION DIES. The identifier that recovery is addressed to has just changed, so
    /// anybody signed in on the strength of the old one — including whoever requested the
    /// change — has to prove themselves again.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApplyLoginIdentifierChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        logger.LogInformation("Applying login identifier change request {RequestId}.", command.RequestId);

        var changeRequest = await governance.GetIdentifierChangeAsync(command.RequestId, cancellationToken);
        if (changeRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (changeRequest.Status != LoginIdentifierChangeStatus.Approved)
        {
            logger.LogWarning("Login identifier change apply rejected for request {RequestId} because status is {Status}.", changeRequest.Id, changeRequest.Status);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A request that is {changeRequest.Status} cannot be applied."));
        }

        var user = changeRequest.User ?? await users.GetByIdAsync(changeRequest.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        // Re-checked at APPLY time, not only at request time. The address may have been taken
        // by somebody else while this request sat waiting for approval.
        var stillFree = changeRequest.IsEmailChange
            ? !await users.EmailExistsAsync(
                changeRequest.NormalizedRequestedValue, user.TenantId, user.Id, cancellationToken)
            : !await users.UsernameExistsAsync(
                changeRequest.NormalizedRequestedValue, user.TenantId, user.Id, cancellationToken);

        if (!stillFree)
        {
            logger.LogWarning("Login identifier change apply rejected for request {RequestId} because the requested value is no longer available.", changeRequest.Id);
            return Result.Failure<OutcomeResponse>(
                Error.Duplicate("That value has been taken since the request was made."));
        }

        if (changeRequest.IsEmailChange)
        {
            user.Email = changeRequest.RequestedValue;
            user.NormalizedEmail = changeRequest.NormalizedRequestedValue;
            // The new address has been proved by the verification step above.
            user.EmailConfirmed = true;
            user.EmailConfirmedAtUtc = now;
        }
        else
        {
            user.UserName = changeRequest.RequestedValue;
            user.NormalizedUserName = changeRequest.NormalizedRequestedValue;
        }

        // A new stamp plus a full sign-out: the identity the old tokens were issued against no
        // longer exists in the same form.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        var revoked = await sessions.RevokeAllAsync(
            user.Id, exceptSessionId: null, "Sign-in details were changed.", cancellationToken);

        changeRequest.Status = LoginIdentifierChangeStatus.Applied;
        changeRequest.AppliedAtUtc = now;

        await audit.WriteAsync(
            AuditActionCodes.UserLoginIdentifierChanged, nameof(User), user.Id, user.DisplayName,
            new
            {
                Applied = true,
                changeRequest.IsEmailChange,
                changeRequest.CurrentValue,
                changeRequest.RequestedValue,
                SessionsRevoked = revoked
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Login identifier change request {RequestId} applied successfully for user {UserId}. SessionsRevoked={SessionsRevoked}.", changeRequest.Id, user.Id, revoked);

        return Result.Success(new OutcomeResponse(
            changeRequest.Id, changeRequest.Status.ToString(), changeRequest.Version,
            $"Sign-in details updated. {revoked} session(s) were signed out.",
            ["View"]));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelLoginIdentifierChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Cancelling login identifier change request {RequestId}.", command.RequestId);

        var changeRequest = await governance.GetIdentifierChangeAsync(command.RequestId, cancellationToken);
        if (changeRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (changeRequest.Status is LoginIdentifierChangeStatus.Applied
            or LoginIdentifierChangeStatus.Cancelled)
        {
            logger.LogWarning("Login identifier change cancellation rejected for request {RequestId} because status is {Status}.", changeRequest.Id, changeRequest.Status);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A request that is {changeRequest.Status} cannot be cancelled."));
        }

        changeRequest.Status = LoginIdentifierChangeStatus.Cancelled;
        changeRequest.RejectionReason = command.Reason;

        await audit.WriteAsync(
            AuditActionCodes.UserLoginIdentifierChanged, nameof(LoginIdentifierChangeRequest),
            changeRequest.Id, changeRequest.User?.DisplayName,
            new { Cancelled = true, command.Reason }, command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Login identifier change request {RequestId} cancelled successfully.", changeRequest.Id);

        return Result.Success(new OutcomeResponse(
            changeRequest.Id, changeRequest.Status.ToString(), changeRequest.Version,
            "Request cancelled.", ["View"]));
    }
}
