using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.Commands.BulkUserAdministration;

/// <summary>Creates and validates a bulk job.</summary>
public sealed record CreateBulkOperationCommand(CreateBulkOperationRequest Request);

/// <summary>Applies a validated job.</summary>
public sealed record ApplyBulkOperationCommand(ApplyBulkOperationRequest Request);

/// <summary>Cancels a job before it runs.</summary>
public sealed record CancelBulkOperationCommand(Guid Id, string Reason);

/// <summary>
/// IAM-USR-06: bulk user administration.
///
/// VALIDATE, THEN APPLY, AND NEVER BOTH AT ONCE BY ACCIDENT. Creating a job validates every
/// row and writes NOTHING to the users. The operator sees "12 of these 400 rows will fail,
/// here is why" while it is still cheap to fix. Applying a 400-row change and reporting the
/// failures afterwards is a far worse experience and frequently not reversible.
///
/// PARTIAL SUCCESS IS A REAL OUTCOME. If 397 succeed and 3 fail the job is
/// PartiallySucceeded, not Failed — reporting failure would send somebody to undo work that
/// actually landed.
///
/// EVERY ROW IS TENANT-SCOPED by the query filter, so a user id from another Organisation
/// simply is not found and the row fails validation rather than reaching across the boundary.
/// </summary>
public sealed class BulkUserAdministrationCommandHandler(
    IBulkOperationRepository operations,
    IUserRepository users,
    IRoleRepository roles,
    ISessionTokenService sessions,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OutcomeResponse>> HandleAsync(
        CreateBulkOperationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        var userIds = (request.UserIds ?? []).Distinct().ToList();

        if (userIds.Count == 0)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Choose at least one user.",
                    [new ValidationError(nameof(request.UserIds), "No users were selected.")]));
        }

        // The actions that need a role must name one, or the job would validate and then have
        // nothing to do.
        if (request.ActionType is BulkActionType.AssignRole or BulkActionType.RemoveRole
            && !request.RoleId.HasValue)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Choose a role for this action.",
                    [new ValidationError(nameof(request.RoleId), "A role is required.")]));
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

        var operation = new BulkOperation
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            OperationNumber = await operations.NextOperationNumberAsync(tenantId, cancellationToken),
            ActionType = request.ActionType,
            Status = BulkOperationStatus.Validating,
            SourceFileName = request.SourceFileName,
            SourceStoragePath = request.SourceStoragePath,
            ActionParameters = System.Text.Json.JsonSerializer.Serialize(new
            {
                request.RoleId,
                request.AccessEndsAtUtc,
                request.Reason
            }),
            TotalItemCount = userIds.Count,
            RequestedByUserId = currentUser.UserId,
            CorrelationId = currentUser.CorrelationId
        };

        await operations.AddAsync(operation, cancellationToken);

        // ---- Validate every row. Nothing is written to the users here. -------------------
        var subjects = await users.GetManyAsync(userIds, cancellationToken);
        var byId = subjects.ToDictionary(user => user.Id);

        var items = new List<BulkOperationItem>(userIds.Count);
        var rowNumber = 0;
        var validCount = 0;

        foreach (var userId in userIds)
        {
            rowNumber++;

            var item = new BulkOperationItem
            {
                TenantId = tenantId,
                BusinessUnitId = tenantContext.BusinessUnitId,
                BulkOperationId = operation.Id,
                RowNumber = rowNumber,
                UserId = userId,
                SourceIdentifier = userId.ToString()
            };

            if (!byId.TryGetValue(userId, out var subject))
            {
                // Not found means "not in this Organisation" — the filter already excluded
                // anything outside it, which is exactly the answer we want.
                item.IsValid = false;
                item.ValidationMessage = "That user was not found in this organisation.";
            }
            else
            {
                item.SourceIdentifier = subject.Email ?? subject.Code;

                var failure = ValidateRow(subject, request.ActionType, now);

                item.IsValid = failure is null;
                item.ValidationMessage = failure;
            }

            if (item.IsValid)
            {
                validCount++;
            }

            items.Add(item);
        }

        await operations.AddItemsAsync(items, cancellationToken);

        operation.ValidatedAtUtc = now;
        operation.Status = validCount == 0
            ? BulkOperationStatus.Failed
            : BulkOperationStatus.Validated;

        if (validCount == 0)
        {
            operation.FailureSummary = "No rows passed validation.";
        }

        await audit.WriteAsync(
            AuditActionCodes.UserBulkOperation, nameof(BulkOperation), operation.Id,
            operation.OperationNumber,
            new { operation.ActionType, operation.TotalItemCount, ValidRows = validCount },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Applying immediately is opt-in, so the default is always "look before you leap".
        if (request.ApplyImmediately && validCount > 0)
        {
            return await ApplyAsync(operation.Id, operation.Version, cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            operation.Id,
            operation.Status.ToString(),
            operation.Version,
            validCount == userIds.Count
                ? $"All {validCount} row(s) passed validation. Review and apply."
                : $"{validCount} of {userIds.Count} row(s) passed validation. "
                  + $"{userIds.Count - validCount} would fail - review before applying.",
            validCount > 0 ? ["View", "Apply", "Cancel"] : ["View", "Cancel"]));
    }

    public Task<Result<OutcomeResponse>> HandleAsync(
        ApplyBulkOperationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(command.Request.OperationId, command.Request.ExpectedVersion, cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelBulkOperationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var operation = await operations.GetAsync(command.Id, cancellationToken);
        if (operation is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That job was not found."));
        }

        if (operation.IsTerminal)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A job that is {operation.Status} cannot be cancelled."));
        }

        operation.Status = BulkOperationStatus.Cancelled;
        operation.CancelledAtUtc = clock.UtcNow;
        operation.CancellationReason = command.Reason;

        await audit.WriteAsync(
            AuditActionCodes.UserBulkOperation, nameof(BulkOperation), operation.Id,
            operation.OperationNumber, new { Cancelled = true, command.Reason },
            command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            operation.Id, operation.Status.ToString(), operation.Version, "Job cancelled.", ["View"]));
    }

    /// <summary>
    /// Applies the validated rows.
    ///
    /// Each row is applied independently and records its own outcome, so one bad row does not
    /// abandon the other 399. The job ends Completed, PartiallySucceeded or Failed depending
    /// on what actually happened.
    /// </summary>
    private async Task<Result<OutcomeResponse>> ApplyAsync(
        Guid operationId, long expectedVersion, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var operation = await operations.GetWithItemsAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That job was not found."));
        }

        if (operation.Version != expectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (operation.Status != BulkOperationStatus.Validated)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A job that is {operation.Status} cannot be applied. Validate it first."));
        }

        operation.Status = BulkOperationStatus.Running;
        operation.StartedAtUtc = now;

        var parameters = ReadParameters(operation.ActionParameters);

        var subjectIds = operation.Items
            .Where(item => item.IsValid && item.UserId.HasValue)
            .Select(item => item.UserId!.Value)
            .ToList();

        var subjects = (await users.GetManyAsync(subjectIds, cancellationToken))
            .ToDictionary(user => user.Id);

        foreach (var item in operation.Items)
        {
            item.ProcessedAtUtc = now;
            item.IsProcessed = true;

            if (!item.IsValid || !item.UserId.HasValue)
            {
                item.Succeeded = false;
                item.ResultMessage = item.ValidationMessage ?? "The row failed validation.";
                continue;
            }

            if (!subjects.TryGetValue(item.UserId.Value, out var subject))
            {
                item.Succeeded = false;
                item.ResultMessage = "That user is no longer available.";
                continue;
            }

            // Re-checked at APPLY time, not only at validation time. The account may have
            // changed in between, and applying a stale decision is how a suspended user gets
            // quietly reactivated.
            var failure = ValidateRow(subject, operation.ActionType, now);

            if (failure is not null)
            {
                item.Succeeded = false;
                item.WasSkipped = true;
                item.ResultMessage = failure;
                continue;
            }

            try
            {
                await ApplyToUserAsync(subject, operation, parameters, now, cancellationToken);

                item.Succeeded = true;
                item.ResultMessage = "Applied.";
            }
            catch (InvalidOperationException exception)
            {
                item.Succeeded = false;
                item.ResultMessage = exception.Message;
            }
        }

        operation.ProcessedItemCount = operation.Items.Count(item => item.IsProcessed);
        operation.SucceededItemCount = operation.Items.Count(item => item.Succeeded);
        operation.SkippedItemCount = operation.Items.Count(item => item.WasSkipped);
        operation.FailedItemCount = operation.Items.Count(
            item => item.IsProcessed && !item.Succeeded && !item.WasSkipped);

        operation.CompletedAtUtc = now;

        // Three outcomes, because "397 of 400 worked" is neither a success nor a failure and
        // calling it either would mislead whoever reads the result.
        operation.Status = operation.SucceededItemCount switch
        {
            0 => BulkOperationStatus.Failed,
            var succeeded when succeeded == operation.TotalItemCount => BulkOperationStatus.Completed,
            _ => BulkOperationStatus.PartiallySucceeded
        };

        if (operation.FailedItemCount > 0)
        {
            operation.FailureSummary = $"{operation.FailedItemCount} row(s) failed. See the item list.";
        }

        await audit.WriteAsync(
            AuditActionCodes.UserBulkOperation, nameof(BulkOperation), operation.Id,
            operation.OperationNumber,
            new
            {
                Applied = true,
                operation.ActionType,
                operation.SucceededItemCount,
                operation.FailedItemCount,
                operation.SkippedItemCount
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            operation.Id,
            operation.Status.ToString(),
            operation.Version,
            $"{operation.SucceededItemCount} succeeded, {operation.FailedItemCount} failed, "
            + $"{operation.SkippedItemCount} skipped.",
            ["View"]));
    }

    /// <summary>Carries out one action on one user.</summary>
    private async Task ApplyToUserAsync(
        User subject,
        BulkOperation operation,
        BulkParameters parameters,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (operation.ActionType)
        {
            case BulkActionType.Suspend:
                subject.Status = UserStatus.Suspended;
                subject.LockoutReason = parameters.Reason;
                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                await sessions.RevokeAllAsync(
                    subject.Id, null, parameters.Reason ?? "Suspended in bulk.", cancellationToken);
                break;

            case BulkActionType.Reactivate:
                subject.Status = string.IsNullOrEmpty(subject.PasswordHash)
                    ? UserStatus.Invited
                    : UserStatus.Active;
                subject.LockoutEnd = null;
                subject.AccessFailedCount = 0;
                subject.IsLockedOutByAdministrator = false;
                subject.LockoutReason = null;
                break;

            case BulkActionType.Deactivate:
                subject.Status = UserStatus.Deactivated;
                subject.ExitedOn ??= now;
                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                await sessions.RevokeAllAsync(
                    subject.Id, null, parameters.Reason ?? "Deactivated in bulk.", cancellationToken);
                break;

            case BulkActionType.Activate:
                subject.Status = UserStatus.Active;
                break;

            case BulkActionType.AssignRole when parameters.RoleId.HasValue:
            {
                var existing = await roles.GetActiveAssignmentAsync(
                    subject.Id, parameters.RoleId.Value, cancellationToken);

                // Already held is a SKIP rather than a failure: the end state the operator
                // asked for is already true.
                if (existing is not null)
                {
                    throw new InvalidOperationException("They already hold that role.");
                }

                await roles.AddUserRoleAsync(new UserRole
                {
                    TenantId = subject.TenantId,
                    BusinessUnitId = subject.BusinessUnitId,
                    UserId = subject.Id,
                    RoleId = parameters.RoleId.Value,
                    Status = UserRoleAssignmentStatus.Active,
                    AssignedAtUtc = now,
                    AssignedByUserId = currentUser.UserId,
                    EffectiveFromUtc = now,
                    EffectiveToUtc = parameters.AccessEndsAtUtc,
                    Justification = parameters.Reason ?? $"Assigned by bulk job {operation.OperationNumber}."
                }, cancellationToken);

                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                break;
            }

            case BulkActionType.RemoveRole when parameters.RoleId.HasValue:
            {
                var assignment = await roles.GetActiveAssignmentAsync(
                    subject.Id, parameters.RoleId.Value, cancellationToken);

                if (assignment is null)
                {
                    throw new InvalidOperationException("They do not hold that role.");
                }

                assignment.Status = UserRoleAssignmentStatus.Revoked;
                assignment.RevokedAtUtc = now;
                assignment.RevokedByUserId = currentUser.UserId;
                assignment.RevocationReason =
                    parameters.Reason ?? $"Removed by bulk job {operation.OperationNumber}.";

                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                break;
            }

            case BulkActionType.ForceSignOut:
                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                await sessions.RevokeAllAsync(
                    subject.Id, null, parameters.Reason ?? "Signed out in bulk.", cancellationToken);
                break;

            case BulkActionType.RequireMfaReset:
                subject.MfaEnabled = false;
                subject.AuthenticatorSecret = null;
                subject.MfaEnrolledAtUtc = null;
                subject.RecoveryCodesRemaining = 0;
                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                break;

            case BulkActionType.ExtendAccess:
                subject.AccessEndsAtUtc = parameters.AccessEndsAtUtc;

                if (subject.Status == UserStatus.Expired && !subject.IsOutsideAccessWindow(now))
                {
                    subject.Status = UserStatus.Active;
                }

                break;

            case BulkActionType.ResetPassword:
                subject.MustChangePassword = true;
                subject.SecurityStamp = Guid.NewGuid().ToString("N");
                break;

            case BulkActionType.Invite:
                if (subject.Status == UserStatus.Draft)
                {
                    subject.Status = UserStatus.Invited;
                }

                break;

            case BulkActionType.Export:
                // Nothing to change; the export is produced from the item list afterwards.
                break;
        }
    }

    /// <summary>
    /// Whether one row can take one action, with a message explaining why not.
    ///
    /// Run at validation AND at apply time, so a change made in between is caught rather than
    /// applied blindly.
    /// </summary>
    private string? ValidateRow(User subject, BulkActionType action, DateTimeOffset now)
    {
        if (subject.IsSystemAccount)
        {
            return "System accounts cannot be changed.";
        }

        // Nobody bulk-changes themselves. It is always an accident, and the person who did it
        // is then the one person who cannot undo it.
        if (subject.Id == currentUser.UserId)
        {
            return "You cannot include your own account in a bulk action.";
        }

        return action switch
        {
            BulkActionType.Suspend when subject.Status == UserStatus.Suspended =>
                "Already suspended.",
            BulkActionType.Suspend when subject.Status is UserStatus.Deactivated or UserStatus.Withdrawn =>
                "That account is not active.",

            BulkActionType.Reactivate when subject.Status is UserStatus.Active =>
                "Already active.",

            BulkActionType.Deactivate when subject.Status == UserStatus.Deactivated =>
                "Already deactivated.",

            BulkActionType.ResetPassword when string.IsNullOrEmpty(subject.PasswordHash) =>
                "That account has no password yet - send an invitation instead.",

            BulkActionType.RequireMfaReset when !subject.MfaEnabled =>
                "That account has no verification method enrolled.",

            BulkActionType.Invite when subject.Status is not (UserStatus.Draft or UserStatus.Withdrawn) =>
                "That account has already been invited or activated.",

            _ => null
        };
    }

    private static BulkParameters ReadParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BulkParameters(null, null, null);
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<BulkParameters>(json);
            return parsed ?? new BulkParameters(null, null, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new BulkParameters(null, null, null);
        }
    }

    /// <summary>The action arguments, stored on the job so applying is deterministic.</summary>
    private sealed record BulkParameters(Guid? RoleId, DateTimeOffset? AccessEndsAtUtc, string? Reason);
}
