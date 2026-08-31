using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.Commands.UserAccess;

/// <summary>Replaces a user role set with the one supplied.</summary>
public sealed record AssignUserRolesCommand(Guid UserId, AssignUserRolesRequest Request);

/// <summary>Replaces a user narrowing data scopes.</summary>
public sealed record AssignUserDataScopesCommand(Guid UserId, AssignUserDataScopesRequest Request);

/// <summary>IAM-USR-03. Asks what a role change would do, without doing it.</summary>
public sealed record PreviewUserAccessCommand(Guid UserId, PreviewUserAccessRequest Request);

/// <summary>
/// Role and scope assignment.
///
/// THE WHOLE SET IS SENT, NOT A DELTA. A delta computed against a screen somebody opened ten
/// minutes ago is how a role nobody meant to touch quietly disappears. Sending the intended
/// end state makes the outcome unambiguous, and the handler works out what that means:
///
/// <code>
/// in the request, not currently held  -> new assignment
/// currently held, in the request      -> left exactly as it is, keeping its history
/// currently held, not in the request  -> revoked, with the row retained
/// </code>
///
/// The middle case matters: re-sending an existing role must NOT close and reopen it, or
/// every save would litter the audit trail with churn and reset the assignment date.
///
/// ASSIGNMENTS ARE REVOKED, NEVER DELETED. An access review next quarter needs to see what
/// somebody used to hold, so the row stays with a Revoked status and a reason.
/// </summary>
public sealed class UserAccessCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IGovernanceRepository governance,
    IEffectiveAccessService effectiveAccess,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OutcomeResponse>> HandleAsync(
        AssignUserRolesCommand command, CancellationToken cancellationToken)
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

        if (user.IsSystemAccount)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("System accounts cannot have their roles changed."));
        }

        // Nobody edits their own roles. It is the single most direct route to privilege
        // escalation, and a legitimate need goes through an access request instead.
        if (user.Id == currentUser.UserId && !currentUser.IsSuperAdmin)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("You cannot change your own roles. Raise an access request instead."));
        }

        var requestedIds = (request.RoleIds ?? []).Distinct().ToList();

        // A role from ANOTHER Organisation is simply not found — the query filter removed it —
        // so a mismatched count is that check.
        var requestedRoles = requestedIds.Count > 0
            ? await roles.GetManyAsync(requestedIds, cancellationToken)
            : [];

        if (requestedRoles.Count != requestedIds.Count)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("One or more of those roles was not found in this organisation.",
                    [new ValidationError(nameof(request.RoleIds), "Choose roles from this organisation.")]));
        }

        // A PLATFORM role is a different matter, and the count check above does NOT catch it.
        //
        // Platform roles carry TenantId = null, and the User/Role query filter deliberately
        // keeps null-tenant rows visible so a SuperAdmin can load their own record while
        // operating inside an Organisation. That same arm means SUPER_ADMIN is returned by the
        // lookup above, the count matches, and the request travels all the way to the database
        // — where the composite foreign key on (tenant_key, role_id) rejects it.
        //
        // The database holding is the point of that key, but a 500 is the wrong answer to a
        // request that is simply not allowed, and relying on the last line of defence to be the
        // only line is how the next refactor introduces a hole. Refused here, explicitly:
        // platform roles belong to platform accounts, and to nobody inside an Organisation.
        var platformRoles = requestedRoles.Where(role => role.TenantId is null).ToList();

        if (platformRoles.Count > 0 && user.TenantId is not null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden(
                    "Platform roles cannot be granted to a member of an organisation."));
        }

        var notAssignable = requestedRoles.Where(role => !role.IsAssignable).ToList();
        if (notAssignable.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("One or more of those roles is not active.",
                    [.. notAssignable.Select(role =>
                        new ValidationError(nameof(request.RoleIds), $"{role.Name} is not active."))]));
        }

        // ---- Segregation of duties -----------------------------------------------------------
        var conflicts = await effectiveAccess.CheckSegregationOfDutiesAsync(
            user.Id, requestedIds, cancellationToken);

        if (conflicts.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "Those roles cannot be held together: " + string.Join("; ", conflicts)));
        }

        // ---- Reconcile ---------------------------------------------------------------------------
        var existing = await roles.GetUserRolesAsync(user.Id, cancellationToken);
        var live = existing.Where(assignment => assignment.IsEffective(now)).ToList();

        var added = new List<string>();
        var revoked = new List<string>();

        // Revoke what is no longer wanted.
        foreach (var assignment in live.Where(item => !requestedIds.Contains(item.RoleId)))
        {
            assignment.Status = UserRoleAssignmentStatus.Revoked;
            assignment.RevokedAtUtc = now;
            assignment.RevokedByUserId = currentUser.UserId;
            assignment.RevocationReason = request.Justification ?? "Removed during a role change.";
            revoked.Add(assignment.Role?.Code ?? assignment.RoleId.ToString());
        }

        // Add what is new. An already-live assignment is left completely alone.
        foreach (var role in requestedRoles.Where(role => live.All(item => item.RoleId != role.Id)))
        {
            await roles.AddUserRoleAsync(new UserRole
            {
                TenantId = user.TenantId,
                BusinessUnitId = user.BusinessUnitId,
                UserId = user.Id,
                RoleId = role.Id,
                Status = UserRoleAssignmentStatus.Active,
                IsPrimary = request.PrimaryRoleId == role.Id,
                AssignedAtUtc = now,
                AssignedByUserId = currentUser.UserId,
                EffectiveFromUtc = now,
                EffectiveToUtc = request.EffectiveToUtc,
                Justification = request.Justification
            }, cancellationToken);

            added.Add(role.Code);
        }

        // ---- Primary flag ---------------------------------------------------------------------------
        if (request.PrimaryRoleId.HasValue)
        {
            foreach (var assignment in live)
            {
                assignment.IsPrimary = assignment.RoleId == request.PrimaryRoleId.Value;
            }
        }

        // A role change alters what the token should say, so the stamp moves and every
        // existing access token is refused on its next use. Without this, somebody whose
        // approval permission was just removed would keep it until their token expired.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await audit.WriteAsync(
            AuditActionCodes.UserRoleAssigned, nameof(User), user.Id, user.DisplayName,
            new { Added = added, Revoked = revoked, request.Justification },
            request.Justification, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var summary = (added.Count, revoked.Count) switch
        {
            (0, 0) => "No role changes were needed.",
            (> 0, 0) => $"Added {added.Count} role(s).",
            (0, > 0) => $"Removed {revoked.Count} role(s).",
            _ => $"Added {added.Count} and removed {revoked.Count} role(s)."
        };

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version, summary,
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        AssignUserDataScopesCommand command, CancellationToken cancellationToken)
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

        var existing = await governance.GetDataScopesAsync(user.Id, cancellationToken);
        var live = existing.Where(scope => scope.IsEffective(now)).ToList();

        var incoming = (request.DataScopes ?? [])
            .Select(scope => (scope.ScopeType, Value: scope.ScopeValue.Trim(), scope.DisplayLabel, scope.EffectiveToUtc))
            .ToList();

        var added = 0;
        var removed = 0;

        // Revoke the ones no longer wanted.
        foreach (var scope in live.Where(item =>
                     !incoming.Any(candidate =>
                         candidate.ScopeType == item.ScopeType
                         && string.Equals(candidate.Value, item.ScopeValue, StringComparison.Ordinal))))
        {
            scope.RevokedAtUtc = now;
            scope.RevokedByUserId = currentUser.UserId;
            scope.RevocationReason = request.Justification ?? "Removed during a scope change.";
            removed++;
        }

        // Add the new ones.
        foreach (var candidate in incoming.Where(candidate =>
                     !live.Any(item =>
                         item.ScopeType == candidate.ScopeType
                         && string.Equals(item.ScopeValue, candidate.Value, StringComparison.Ordinal))))
        {
            await governance.AddDataScopeAsync(new UserDataScope
            {
                TenantId = user.TenantId ?? tenantContext.RequireTenantId(),
                BusinessUnitId = user.BusinessUnitId,
                UserId = user.Id,
                ScopeType = candidate.ScopeType,
                ScopeValue = candidate.Value,
                DisplayLabel = candidate.DisplayLabel,
                GrantedAtUtc = now,
                GrantedByUserId = currentUser.UserId,
                EffectiveFromUtc = now,
                EffectiveToUtc = candidate.EffectiveToUtc
            }, cancellationToken);

            added++;
        }

        // Scopes travel in the token as data_scope claims, so the stamp moves for the same
        // reason it does on a role change.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await audit.WriteAsync(
            AuditActionCodes.DataScopeGranted, nameof(User), user.Id, user.DisplayName,
            new { Added = added, Removed = removed, request.Justification },
            request.Justification, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            added == 0 && removed == 0
                ? "No scope changes were needed."
                : $"Added {added} and removed {removed} data scope(s).",
            UserMappingConfig.PermittedActionsFor(user, now)));
    }

    /// <summary>
    /// IAM-USR-03. What a proposed role change would gain and lose, WITHOUT committing it.
    ///
    /// The point of the screen: adding one role to somebody who already holds three is not
    /// obviously safe. It may overlap entirely, or quietly hand over an export permission
    /// nobody intended. Showing gained and lost side by side turns that from a guess into a
    /// decision, and flags the sensitive gains that need a justification.
    /// </summary>
    public async Task<Result<UserAccessComparisonResponse>> HandleAsync(
        PreviewUserAccessCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserAccessComparisonResponse>(Error.UserNotFound());
        }

        var proposed = (command.Request.RoleIds ?? []).Distinct().ToList();

        var comparison = await effectiveAccess.PreviewAsync(
            user.Id, user.TenantId ?? tenantContext.TenantId, proposed, cancellationToken);

        var conflicts = await effectiveAccess.CheckSegregationOfDutiesAsync(
            user.Id, proposed, cancellationToken);

        return Result.Success(new UserAccessComparisonResponse(
            comparison.Gained,
            comparison.Lost,
            comparison.Unchanged,
            comparison.SensitiveGained,
            comparison.RequiresJustification,
            conflicts));
    }
}
