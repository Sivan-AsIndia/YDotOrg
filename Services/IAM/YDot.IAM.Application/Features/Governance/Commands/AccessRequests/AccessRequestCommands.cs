using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Governance.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Application.Features.Governance.Commands.AccessRequests;

/// <summary>Raises an access request.</summary>
public sealed record CreateAccessRequestCommand(CreateAccessRequestRequest Request);

/// <summary>Edits a draft.</summary>
public sealed record UpdateAccessRequestCommand(Guid Id, UpdateAccessRequestRequest Request);

/// <summary>Submits a draft for decision.</summary>
public sealed record SubmitAccessRequestCommand(Guid Id, SubmitAccessRequestRequest Request);

/// <summary>Approves or rejects.</summary>
public sealed record DecideAccessRequestCommand(Guid Id, DecideAccessRequestRequest Request);

/// <summary>Withdraws before a decision.</summary>
public sealed record WithdrawAccessRequestCommand(Guid Id, WithdrawAccessRequestRequest Request);

/// <summary>Sends a request back to the requester for more information.</summary>
public sealed record ReturnAccessRequestCommand(Guid Id, ReturnAccessRequestRequest Request);

/// <summary>
/// Access requests: the front door to privilege inside an Organisation.
///
/// TWO RULES SHAPE THIS WHOLE HANDLER.
///
/// FIRST, MAKER AND CHECKER ARE DIFFERENT PEOPLE. The approver may not be the requester, and
/// may not be the subject. That is checked here rather than only on the screen, because a
/// screen check protects a screen and this method is what actually grants the access.
///
/// SECOND, APPROVAL WRITES THE REAL THING. It does not set a flag for somebody to action
/// later — it creates the <see cref="UserRole"/> or <see cref="UserDataScope"/> row and
/// stamps it with this request id. So every grant in the system can be traced back to the
/// justification that earned it, and an access review a year later can read why.
/// </summary>
public sealed class AccessRequestCommandHandler(
    IGovernanceRepository governance,
    IUserRepository users,
    IRoleRepository roles,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IEffectiveAccessService effectiveAccess,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<ClientAppSettings> clientApp,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OutcomeResponse>> HandleAsync(
        CreateAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        var subject = await users.GetByIdAsync(request.RequestedForUserId, cancellationToken);
        if (subject is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        Role? role = null;
        if (request.RoleId.HasValue)
        {
            role = await roles.GetByIdAsync(request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.NotFound("That role was not found in this organisation."));
            }
        }

        // Temporary access without an end date is just permanent access with a promise, so
        // the end date is required when the type says temporary.
        if (request.RequestType == AccessRequestType.TemporaryElevation && !request.AccessEndsAtUtc.HasValue)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Temporary access needs an end date.",
                    [new ValidationError(nameof(request.AccessEndsAtUtc), "Choose when this access should end.")]));
        }

        // Whether what is being asked for is sensitive drives the approval rules downstream.
        var isSensitive = false;
        if (role is not null)
        {
            var rolePermissions = await roles.GetRolePermissionsAsync(role.Id, cancellationToken);
            isSensitive = role.IsPrivileged
                          || role.GrantsAllTenantPermissions
                          || rolePermissions.Any(item => PermissionCodes.IsSensitive(item.PermissionCode));
        }
        else if (!string.IsNullOrWhiteSpace(request.PermissionCode))
        {
            isSensitive = PermissionCodes.IsSensitive(request.PermissionCode);
        }

        var accessRequest = new AccessRequest
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            RequestNumber = await governance.NextRequestNumberAsync(tenantId, cancellationToken),
            RequestedForUserId = subject.Id,
            RequestedByUserId = currentUser.UserId,
            RequestType = request.RequestType,
            RoleId = request.RoleId,
            PermissionCode = request.PermissionCode,
            ScopeType = request.ScopeType,
            ScopeValue = request.ScopeValue,
            BusinessJustification = request.BusinessJustification.Trim(),
            AccessStartsAtUtc = request.AccessStartsAtUtc ?? now,
            AccessEndsAtUtc = request.AccessEndsAtUtc,
            Status = request.SubmitImmediately ? AccessRequestStatus.Submitted : AccessRequestStatus.Draft,
            SubmittedAtUtc = request.SubmitImmediately ? now : null,
            IsSensitive = isSensitive,
            // A request nobody acts on should lapse rather than sit open forever.
            ExpiresAtUtc = now.AddDays(30)
        };

        await governance.AddAccessRequestAsync(accessRequest, cancellationToken);

        await audit.WriteAsync(
            request.SubmitImmediately
                ? AuditActionCodes.AccessRequestSubmitted
                : AuditActionCodes.AccessRequestCreated,
            nameof(AccessRequest), accessRequest.Id, accessRequest.RequestNumber,
            new { accessRequest.RequestType, SubjectId = subject.Id, isSensitive },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.SubmitImmediately)
        {
            await NotifyApproversAsync(accessRequest, subject, cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            request.SubmitImmediately
                ? $"Request {accessRequest.RequestNumber} submitted for approval."
                : $"Request {accessRequest.RequestNumber} saved as a draft.",
            GovernanceMappingConfig.PermittedActionsFor(accessRequest, currentUser.UserId)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accessRequest = await governance.GetAccessRequestAsync(command.Id, cancellationToken);
        if (accessRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (accessRequest.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // Only a draft is editable. Editing something an approver is looking at would mean
        // they approve one thing and something else takes effect.
        if (accessRequest.Status != AccessRequestStatus.Draft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Only a draft request can be edited."));
        }

        if (accessRequest.RequestedByUserId != currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("Only the person who raised a request can edit it."));
        }

        if (!string.IsNullOrWhiteSpace(command.Request.BusinessJustification))
        {
            accessRequest.BusinessJustification = command.Request.BusinessJustification.Trim();
        }

        if (command.Request.RoleId.HasValue)
        {
            accessRequest.RoleId = command.Request.RoleId;
        }

        if (command.Request.AccessStartsAtUtc.HasValue)
        {
            accessRequest.AccessStartsAtUtc = command.Request.AccessStartsAtUtc.Value;
        }

        if (command.Request.AccessEndsAtUtc.HasValue)
        {
            accessRequest.AccessEndsAtUtc = command.Request.AccessEndsAtUtc;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            "Request saved.", GovernanceMappingConfig.PermittedActionsFor(accessRequest, currentUser.UserId)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SubmitAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var accessRequest = await governance.GetAccessRequestAsync(command.Id, cancellationToken);
        if (accessRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (accessRequest.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (accessRequest.Status != AccessRequestStatus.Draft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Only a draft request can be submitted."));
        }

        accessRequest.Status = AccessRequestStatus.Submitted;
        accessRequest.SubmittedAtUtc = now;

        var subject = await users.GetByIdAsync(accessRequest.RequestedForUserId, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.AccessRequestSubmitted, nameof(AccessRequest), accessRequest.Id,
            accessRequest.RequestNumber, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (subject is not null)
        {
            await NotifyApproversAsync(accessRequest, subject, cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            $"Request {accessRequest.RequestNumber} submitted for approval.",
            GovernanceMappingConfig.PermittedActionsFor(accessRequest, currentUser.UserId)));
    }

    /// <summary>
    /// The decision, and the grant that follows an approval.
    ///
    /// Approval does the real work here rather than queueing it: the assignment row is
    /// written in the same transaction as the decision, so there is no window in which a
    /// request is Approved but the person still cannot do the thing.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DecideAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var accessRequest = await governance.GetAccessRequestAsync(command.Id, cancellationToken);
        if (accessRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (accessRequest.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (accessRequest.Status != AccessRequestStatus.Submitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A request that is {accessRequest.Status} cannot be decided."));
        }

        // ---- Segregation of duties ----------------------------------------------------------
        //
        // The approver may be neither the requester nor the subject. SuperAdmin is not exempt:
        // a root user approving their own request is exactly the pattern an auditor looks for.
        if (accessRequest.RequestedByUserId == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot decide a request that you raised."));
        }

        if (accessRequest.RequestedForUserId == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot decide a request for your own access."));
        }

        if (!request.Approved && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Give a reason so the requester knows what to do next.",
                    [new ValidationError(nameof(request.Notes), "A reason is required when rejecting.")]));
        }

        accessRequest.Status = request.Approved ? AccessRequestStatus.Approved : AccessRequestStatus.Rejected;
        accessRequest.DecidedAtUtc = now;
        accessRequest.DecidedByUserId = currentUser.UserId;
        accessRequest.DecisionNotes = request.Notes;

        // The approver may grant less than was asked for.
        if (request.AccessEndsAtUtc.HasValue)
        {
            accessRequest.AccessEndsAtUtc = request.AccessEndsAtUtc;
        }

        var subject = await users.GetByIdAsync(accessRequest.RequestedForUserId, cancellationToken);

        if (request.Approved && subject is not null)
        {
            var granted = await GrantAsync(accessRequest, subject, now, cancellationToken);
            if (granted.IsFailure)
            {
                return Result.Failure<OutcomeResponse>(granted.Error!);
            }
        }

        await audit.WriteAsync(
            request.Approved ? AuditActionCodes.AccessRequestApproved : AuditActionCodes.AccessRequestRejected,
            nameof(AccessRequest), accessRequest.Id, accessRequest.RequestNumber,
            new { request.Approved, request.Notes, accessRequest.RequestedForUserId },
            request.Notes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (subject is not null)
        {
            await NotifyRequesterAsync(accessRequest, request.Approved, request.Notes, cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            request.Approved
                ? $"Request {accessRequest.RequestNumber} approved and the access granted."
                : $"Request {accessRequest.RequestNumber} rejected.",
            GovernanceMappingConfig.PermittedActionsFor(accessRequest, currentUser.UserId)));
    }

    /// <summary>
    /// Sends a request back for more information.
    ///
    /// AVAILABLE TO AN APPROVER, and subject to the same independence rule as a decision: an
    /// approver may not return their own request to themselves, because that would be a way of
    /// keeping a request alive indefinitely without anybody independent ever looking at it.
    ///
    /// The request keeps its number, its history and its return count. Making the requester raise
    /// a fresh one would lose the thread of what was asked and why it was not enough.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReturnAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var accessRequest = await governance.GetAccessRequestAsync(command.Id, cancellationToken);
        if (accessRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That access request was not found."));
        }

        if (accessRequest.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (accessRequest.Status != AccessRequestStatus.Submitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Only a submitted request can be sent back."));
        }

        // The same independence rule that governs a decision. Returning your own request would
        // let it be kept alive indefinitely without anybody independent ever seeing it.
        if (accessRequest.RequestedByUserId == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden(
                "You cannot send your own request back. Withdraw it and raise a new one."));
        }

        if (string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Say what is missing, so it can be corrected.",
                    [new ValidationError(nameof(command.Request.Reason),
                        "A reason is required when sending a request back.")]));
        }

        accessRequest.Status = AccessRequestStatus.Returned;
        accessRequest.ReturnReason = command.Request.Reason.Trim();
        accessRequest.ReturnedAtUtc = now;
        accessRequest.ReturnedByUserId = currentUser.UserId;
        accessRequest.ReturnCount += 1;

        await audit.WriteAsync(
            AuditActionCodes.AccessRequestReturned, nameof(AccessRequest), accessRequest.Id,
            accessRequest.RequestNumber,
            new { accessRequest.ReturnCount, command.Request.Reason },
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            "Sent back to the requester with your note.",
            ["View"]));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        WithdrawAccessRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accessRequest = await governance.GetAccessRequestAsync(command.Id, cancellationToken);
        if (accessRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That request was not found."));
        }

        if (accessRequest.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (accessRequest.IsDecided)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "A decided request cannot be withdrawn."));
        }

        if (accessRequest.RequestedByUserId != currentUser.UserId
            && !currentUser.HasPermission(PermissionCodes.AccessRequestsWithdraw))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("Only the person who raised a request can withdraw it."));
        }

        accessRequest.Status = AccessRequestStatus.Withdrawn;
        accessRequest.WithdrawnAtUtc = clock.UtcNow;
        accessRequest.WithdrawalReason = command.Request.Reason;

        await audit.WriteAsync(
            AuditActionCodes.AccessRequestWithdrawn, nameof(AccessRequest), accessRequest.Id,
            accessRequest.RequestNumber, new { command.Request.Reason },
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            accessRequest.Id, accessRequest.Status.ToString(), accessRequest.Version,
            "Request withdrawn.", GovernanceMappingConfig.PermittedActionsFor(accessRequest, currentUser.UserId)));
    }

    /// <summary>
    /// Carries out an approved request.
    ///
    /// The created row is stamped with <c>SourceAccessRequestId</c>, which is the link an
    /// auditor follows backwards from "why does this person have this?" to the justification.
    /// </summary>
    private async Task<Result> GrantAsync(
        AccessRequest accessRequest, User subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        switch (accessRequest.RequestType)
        {
            case AccessRequestType.RoleAssignment or AccessRequestType.TemporaryElevation
                when accessRequest.RoleId.HasValue:
            {
                // Re-check the conflict rules at APPROVAL time, not only at request time. The
                // person may have picked up another role in between.
                var conflicts = await effectiveAccess.CheckSegregationOfDutiesAsync(
                    subject.Id, [accessRequest.RoleId.Value], cancellationToken);

                if (conflicts.Count > 0)
                {
                    return Result.Failure(Error.SegregationOfDuties(
                        "This role now conflicts with one the person already holds: "
                        + string.Join("; ", conflicts)));
                }

                var existing = await roles.GetActiveAssignmentAsync(
                    subject.Id, accessRequest.RoleId.Value, cancellationToken);

                if (existing is null)
                {
                    var assignment = new UserRole
                    {
                        TenantId = subject.TenantId,
                        BusinessUnitId = subject.BusinessUnitId,
                        UserId = subject.Id,
                        RoleId = accessRequest.RoleId.Value,
                        Status = UserRoleAssignmentStatus.Active,
                        AssignedAtUtc = now,
                        AssignedByUserId = currentUser.UserId,
                        EffectiveFromUtc = accessRequest.AccessStartsAtUtc,
                        EffectiveToUtc = accessRequest.AccessEndsAtUtc,
                        SourceAccessRequestId = accessRequest.Id,
                        Justification = accessRequest.BusinessJustification
                    };

                    await roles.AddUserRoleAsync(assignment, cancellationToken);
                    accessRequest.GrantedUserRoleId = assignment.Id;
                }

                break;
            }

            case AccessRequestType.DataScopeGrant
                when accessRequest.ScopeType.HasValue && !string.IsNullOrWhiteSpace(accessRequest.ScopeValue):
            {
                await governance.AddDataScopeAsync(new UserDataScope
                {
                    TenantId = subject.TenantId ?? accessRequest.TenantId,
                    BusinessUnitId = subject.BusinessUnitId,
                    UserId = subject.Id,
                    ScopeType = accessRequest.ScopeType.Value,
                    ScopeValue = accessRequest.ScopeValue!,
                    GrantedAtUtc = now,
                    GrantedByUserId = currentUser.UserId,
                    EffectiveFromUtc = accessRequest.AccessStartsAtUtc,
                    EffectiveToUtc = accessRequest.AccessEndsAtUtc,
                    SourceAccessRequestId = accessRequest.Id
                }, cancellationToken);

                break;
            }

            case AccessRequestType.PermissionGrant when !string.IsNullOrWhiteSpace(accessRequest.PermissionCode):
            {
                // A single permission with no role behind it becomes a direct user claim.
                await governance.AddUserClaimAsync(new UserClaimEntry
                {
                    TenantId = subject.TenantId,
                    BusinessUnitId = subject.BusinessUnitId,
                    UserId = subject.Id,
                    ClaimType = ClaimTypeNames.Permission,
                    ClaimValue = accessRequest.PermissionCode!,
                    GrantedAtUtc = now,
                    GrantedByUserId = currentUser.UserId,
                    ExpiresAtUtc = accessRequest.AccessEndsAtUtc,
                    Justification = accessRequest.BusinessJustification
                }, cancellationToken);

                break;
            }
        }

        // The grant changes what the person may do, so their existing tokens must stop being
        // trusted or the new access would not appear until the next refresh.
        subject.SecurityStamp = Guid.NewGuid().ToString("N");

        return Result.Success();
    }

    private async Task NotifyApproversAsync(
        AccessRequest accessRequest, User subject, CancellationToken cancellationToken)
    {
        var businessUnit = await businessUnits.GetByIdAsync(accessRequest.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return;
        }

        var tenant = await tenants.GetByIdAsync(accessRequest.TenantId, cancellationToken);

        // APPROVER, not the ACCESS_APPROVER this used to name. That role was one of thirteen
        // job-shaped roles the catalogue no longer seeds; the code below looks the role up by
        // code and returns quietly when it finds nothing, so the rename would not have thrown -
        // it would simply have stopped notifying anybody that a request was waiting.
        var approverRole = await roles.GetByCodeAsync(
            RoleCodes.Approver, accessRequest.TenantId, cancellationToken);

        if (approverRole is null)
        {
            return;
        }

        var members = await roles.GetRoleMembersAsync(approverRole.Id, cancellationToken);
        var approverIds = members
            .Where(assignment => assignment.UserId != accessRequest.RequestedByUserId)
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToList();

        if (approverIds.Count == 0)
        {
            return;
        }

        var approvers = await users.GetManyAsync(approverIds, cancellationToken);

        foreach (var approver in approvers)
        {
            await notifications.SendAccessRequestAwaitingDecisionAsync(
                approver, subject, tenant, businessUnit, accessRequest.RequestNumber,
                // FROM CONFIGURATION, for the same reason as the review path.
                $"{clientApp.Value.AccessRequestPath}?id={accessRequest.Id}",
                cancellationToken);
        }
    }

    private async Task NotifyRequesterAsync(
        AccessRequest accessRequest, bool approved, string? reason, CancellationToken cancellationToken)
    {
        var requester = await users.GetByIdAsync(accessRequest.RequestedByUserId, cancellationToken);
        if (requester is null)
        {
            return;
        }

        var businessUnit = await businessUnits.GetByIdAsync(accessRequest.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return;
        }

        var tenant = await tenants.GetByIdAsync(accessRequest.TenantId, cancellationToken);

        await notifications.SendAccessRequestDecidedAsync(
            requester, tenant, businessUnit, accessRequest.RequestNumber, approved, reason, cancellationToken);
    }

// Permitted actions live in GovernanceMappingConfig, shared with the read service.
}
