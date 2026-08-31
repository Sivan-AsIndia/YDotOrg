using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Roles.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Roles.Commands.ManageRole;

/// <summary>Creates a role inside the caller Organisation.</summary>
public sealed record CreateRoleCommand(CreateRoleRequest Request);

/// <summary>Edits a role.</summary>
public sealed record UpdateRoleCommand(Guid RoleId, UpdateRoleRequest Request);

/// <summary>Replaces a role permission set.</summary>
public sealed record AssignRolePermissionsCommand(Guid RoleId, AssignRolePermissionsRequest Request);

/// <summary>Activates or deactivates a role.</summary>
public sealed record ChangeRoleStatusCommand(Guid RoleId, ChangeRoleStatusRequest Request);

/// <summary>Deletes a role. Refused when anybody holds it.</summary>
public sealed record DeleteRoleCommand(Guid RoleId, DeleteRoleRequest Request);

/// <summary>Declares that two roles may not be held together.</summary>
public sealed record CreateRoleIncompatibilityCommand(CreateRoleIncompatibilityRequest Request);

/// <summary>Removes a segregation-of-duties rule.</summary>
public sealed record DeleteRoleIncompatibilityCommand(Guid Id);

/// <summary>Replaces the claims carried by holders of a role.</summary>
public sealed record AssignRoleClaimsCommand(Guid RoleId, AssignRoleClaimsRequest Request);

/// <summary>
/// Role management.
///
/// THE RULE THAT MATTERS MOST HERE IS THE PLATFORM-CODE GUARD. A Tenant role may only carry
/// permissions drawn from the Tenant-assignable catalogue, so no amount of role editing can
/// hand an Organisation <c>platform.organisations.approve</c> and let it approve itself. The
/// check is in <see cref="ApplyPermissionsAsync"/>, and it is a refusal rather than a silent
/// filter — quietly dropping a requested permission would leave an administrator believing
/// they had granted something they had not.
///
/// SYSTEM ROLES ARE PROTECTED, BUT NOT FROZEN. They cannot be deleted or renamed, because
/// seed data and the invitation flow refer to them by code. Their permission set CAN be
/// adjusted, because an Organisation may legitimately decide its own administrator should
/// not, say, export.
/// </summary>
public sealed class RoleCommandHandler(
    IRoleRepository roles,
    IPermissionRepository permissions,
    IMenuRepository menus,
    IUserRepository users,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RoleDetailResponse>> HandleAsync(
        CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<RoleDetailResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? CodeValue.FromName(request.Name)
            : CodeValue.TryParse(request.Code)?.Value;

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<RoleDetailResponse>(
                Error.Validation("That role code is not valid.",
                    [new ValidationError(nameof(request.Code),
                        "Use upper-case letters, digits, underscores or hyphens.")]));
        }

        if (await roles.CodeExistsAsync(code, tenantId, null, cancellationToken))
        {
            return Result.Failure<RoleDetailResponse>(
                Error.Duplicate($"A role with code {code} already exists in this organisation."));
        }

        if (await roles.NameExistsAsync(request.Name.Trim().ToUpperInvariant(), tenantId, null, cancellationToken))
        {
            return Result.Failure<RoleDetailResponse>(
                Error.Duplicate("A role with that name already exists in this organisation."));
        }

        var role = new Role
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            Code = code,
            NormalizedCode = code.ToUpperInvariant(),
            Name = request.Name.Trim(),
            NormalizedName = request.Name.Trim().ToUpperInvariant(),
            Description = request.Description?.Trim(),
            RoleType = RoleType.Tenant,
            Status = request.Status,
            Priority = request.Priority,
            IsPrivileged = request.IsPrivileged,
            IsDefaultRole = request.IsDefaultRole,
            DisplayTag = request.DisplayTag,
            IsSystemRole = false,
            // Only the seeded TenantAdmin carries the blanket grant. A role created through
            // the UI never does, or anybody with role-create could grant themselves everything.
            GrantsAllTenantPermissions = false
        };

        await roles.AddAsync(role, cancellationToken);

        if (request.IsDefaultRole)
        {
            await ClearOtherDefaultsAsync(tenantId, role.Id, cancellationToken);
        }

        if (request.PermissionCodes is { Count: > 0 })
        {
            var applied = await ApplyPermissionsAsync(
                role, request.PermissionCodes, [], now, cancellationToken);

            if (applied.IsFailure)
            {
                return Result.Failure<RoleDetailResponse>(applied.Error!);
            }
        }

        if (request.VisibleMenuIds is { Count: > 0 })
        {
            await ApplyMenuMappingAsync(role, request.VisibleMenuIds, now, cancellationToken);
        }

        await audit.WriteAsync(
            AuditActionCodes.RoleCreated, nameof(Role), role.Id, role.Name,
            new { role.Code, PermissionCount = request.PermissionCodes?.Count ?? 0 },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(role.ToDetailResponse([], [], [], [], 0));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        if (role.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!string.IsNullOrWhiteSpace(request.Name)
            && !string.Equals(role.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            var normalised = request.Name.Trim().ToUpperInvariant();

            if (await roles.NameExistsAsync(normalised, role.TenantId, role.Id, cancellationToken))
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Duplicate("A role with that name already exists in this organisation."));
            }

            role.Name = request.Name.Trim();
            role.NormalizedName = normalised;
        }

        if (request.Description is not null)
        {
            role.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.Priority.HasValue)
        {
            role.Priority = request.Priority.Value;
        }

        if (request.IsPrivileged.HasValue)
        {
            role.IsPrivileged = request.IsPrivileged.Value;
        }

        if (request.DisplayTag is not null)
        {
            role.DisplayTag = string.IsNullOrWhiteSpace(request.DisplayTag) ? null : request.DisplayTag;
        }

        if (request.IsDefaultRole.HasValue && request.IsDefaultRole.Value != role.IsDefaultRole)
        {
            role.IsDefaultRole = request.IsDefaultRole.Value;

            if (role.IsDefaultRole && role.TenantId.HasValue)
            {
                await ClearOtherDefaultsAsync(role.TenantId.Value, role.Id, cancellationToken);
            }
        }

        await audit.WriteAsync(
            AuditActionCodes.RoleUpdated, nameof(Role), role.Id, role.Name,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            role.Id, role.Status.ToString(), role.Version, "Role saved.",
            RoleMappingConfig.PermittedActionsFor(role, 0)));
    }

    /// <summary>
    /// Replaces a role permission set.
    ///
    /// The whole set is sent, for the same reason as user roles: a delta computed against a
    /// stale screen quietly loses permissions nobody meant to touch.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        AssignRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var role = await roles.GetWithPermissionsAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        if (role.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // Editing a blanket-grant role permission list is meaningless: the flag is the grant,
        // and a list beside it would only mislead.
        if (role.GrantsAllTenantPermissions)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This role already grants every permission in the organisation, so its permission list cannot be edited."));
        }

        var applied = await ApplyPermissionsAsync(
            role, request.PermissionCodes ?? [], request.DeniedPermissionCodes ?? [], now, cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(applied.Error!);
        }

        // Changing a role changes what its holders can do, so every one of their tokens has
        // to stop being trusted. Stamping each holder is the price of immediate revocation.
        await InvalidateHoldersAsync(role.Id, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.RolePermissionsChanged, nameof(Role), role.Id, role.Name,
            new
            {
                Granted = request.PermissionCodes?.Count ?? 0,
                Denied = request.DeniedPermissionCodes?.Count ?? 0,
                request.Justification
            },
            request.Justification, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            role.Id, role.Status.ToString(), role.Version,
            $"Role permissions saved. {request.PermissionCodes?.Count ?? 0} permission(s) granted.",
            RoleMappingConfig.PermittedActionsFor(role, 0)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeRoleStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        if (role.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // Deactivating the administrator role is how an Organisation locks itself out.
        if (role.IsSystemRole && command.Request.Status != RoleStatus.Active
            && role.Code == RoleCodes.TenantAdmin)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("The organisation administrator role cannot be deactivated."));
        }

        role.Status = command.Request.Status;

        if (command.Request.Status != RoleStatus.Active)
        {
            // Existing holders keep the assignment row but stop getting the permissions,
            // because the effective-access resolver ignores an inactive role. Their tokens
            // still have to be invalidated for that to bite immediately.
            await InvalidateHoldersAsync(role.Id, cancellationToken);
        }

        await audit.WriteAsync(
            command.Request.Status == RoleStatus.Active
                ? AuditActionCodes.RoleActivated
                : AuditActionCodes.RoleDeactivated,
            nameof(Role), role.Id, role.Name,
            new { NewStatus = command.Request.Status.ToString(), command.Request.Reason },
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            role.Id, role.Status.ToString(), role.Version,
            command.Request.Status == RoleStatus.Active ? "Role activated." : "Role deactivated.",
            RoleMappingConfig.PermittedActionsFor(role, 0)));
    }

    /// <summary>
    /// Deletes a role.
    ///
    /// Refused when anybody holds it, and refused for a system role. Deactivation is the
    /// route for a role that is no longer wanted but has history: deleting it would orphan
    /// every audit row and access review that names it.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        if (role.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (role.IsSystemRole)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("A system role cannot be deleted. Deactivate it instead."));
        }

        var holders = await roles.CountAssignmentsAsync(role.Id, cancellationToken);
        if (holders > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"{holders} user(s) still hold this role. Remove them first, or deactivate the role instead."));
        }

        roles.Remove(role);

        await audit.WriteAsync(
            AuditActionCodes.RoleDeleted, nameof(Role), role.Id, role.Name,
            new { role.Code, command.Request.Reason }, command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            role.Id, "Deleted", 0, "Role deleted.", []));
    }

    public async Task<Result<RoleIncompatibilityResponse>> HandleAsync(
        CreateRoleIncompatibilityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (request.RoleId == request.ConflictingRoleId)
        {
            return Result.Failure<RoleIncompatibilityResponse>(
                Error.Validation("A role cannot conflict with itself.",
                    [new ValidationError(nameof(request.ConflictingRoleId), "Choose a different role.")]));
        }

        var both = await roles.GetManyAsync([request.RoleId, request.ConflictingRoleId], cancellationToken);
        if (both.Count != 2)
        {
            return Result.Failure<RoleIncompatibilityResponse>(
                Error.NotFound("One or both of those roles was not found in this organisation."));
        }

        var rule = new RoleIncompatibility
        {
            TenantId = tenantContext.RequireTenantId(),
            BusinessUnitId = tenantContext.BusinessUnitId,
            RoleId = request.RoleId,
            ConflictingRoleId = request.ConflictingRoleId,
            Reason = request.Reason.Trim(),
            IsBlocking = request.IsBlocking,
            IsActive = true
        };

        await roles.AddIncompatibilityAsync(rule, cancellationToken);

        var first = both.First(role => role.Id == request.RoleId);
        var second = both.First(role => role.Id == request.ConflictingRoleId);

        await audit.WriteAsync(
            AuditActionCodes.RoleUpdated, nameof(RoleIncompatibility), rule.Id,
            $"{first.Name ?? first.Code} / {second.Name ?? second.Code}",
            new { request.Reason, request.IsBlocking }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RoleIncompatibilityResponse(
            rule.Id, first.Id, first.Name ?? first.Code, second.Id, second.Name ?? second.Code,
            rule.Reason, rule.IsBlocking, rule.IsActive));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteRoleIncompatibilityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await roles.GetIncompatibilityAsync(command.Id, cancellationToken);
        if (rule is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That rule was not found."));
        }

        roles.RemoveIncompatibility(rule);

        await audit.WriteAsync(
            AuditActionCodes.RoleUpdated, nameof(RoleIncompatibility), rule.Id,
            null, new { Removed = true }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(rule.Id, "Deleted", 0, "Rule removed.", []));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        AssignRoleClaimsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        if (role.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        var existing = await roles.GetRoleClaimsAsync(role.Id, cancellationToken);
        roles.RemoveRoleClaims(existing);

        foreach (var claim in command.Request.Claims ?? [])
        {
            await roles.AddRoleClaimAsync(new RoleClaimEntry
            {
                TenantId = role.TenantId,
                BusinessUnitId = role.BusinessUnitId,
                RoleId = role.Id,
                ClaimType = claim.ClaimType.Trim(),
                ClaimValue = claim.ClaimValue.Trim(),
                Description = claim.Description
            }, cancellationToken);
        }

        await InvalidateHoldersAsync(role.Id, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.RoleUpdated, nameof(Role), role.Id, role.Name,
            new { ClaimCount = command.Request.Claims?.Count ?? 0 },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            role.Id, role.Status.ToString(), role.Version, "Role claims saved.",
            RoleMappingConfig.PermittedActionsFor(role, 0)));
    }

    // =================================================================================
    // Shared
    // =================================================================================

    /// <summary>
    /// Replaces the permission rows on a role.
    ///
    /// THE PLATFORM GUARD LIVES HERE. Only codes from the Tenant-assignable catalogue are
    /// accepted, and a request naming a platform-only code is REFUSED rather than silently
    /// filtered — an administrator who was quietly given less than they asked for would
    /// believe they had granted something they had not.
    /// </summary>
    private async Task<Result> ApplyPermissionsAsync(
        Role role,
        IReadOnlyList<string> grantedCodes,
        IReadOnlyList<string> deniedCodes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requested = grantedCodes.Concat(deniedCodes).Distinct(StringComparer.Ordinal).ToList();

        var platformCodes = requested.Where(PermissionCodes.IsPlatformOnly).ToList();
        if (platformCodes.Count > 0)
        {
            return Result.Failure(Error.Forbidden(
                "These permissions belong to the platform and cannot be given to an organisation role: "
                + string.Join(", ", platformCodes)));
        }

        var assignable = await permissions.GetTenantAssignableAsync(cancellationToken);
        var byCode = assignable.ToDictionary(permission => permission.Code, StringComparer.Ordinal);

        var unknown = requested.Where(code => !byCode.ContainsKey(code)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure(Error.Validation(
                "One or more of those permissions was not recognised.",
                [.. unknown.Select(code => new ValidationError("PermissionCodes", $"Unknown permission: {code}"))]));
        }

        var existing = await roles.GetRolePermissionsAsync(role.Id, cancellationToken);
        roles.RemoveRolePermissions(existing);

        foreach (var code in grantedCodes.Distinct(StringComparer.Ordinal))
        {
            await roles.AddRolePermissionAsync(new RolePermission
            {
                TenantId = role.TenantId ?? tenantContext.RequireTenantId(),
                BusinessUnitId = role.BusinessUnitId,
                RoleId = role.Id,
                PermissionId = byCode[code].Id,
                PermissionCode = code,
                IsDenied = false,
                GrantedAtUtc = now,
                GrantedByUserId = currentUser.UserId
            }, cancellationToken);
        }

        // Deny rows are written last and beat any allow, so one permission can be carved out
        // of a broad role without unpicking the role.
        foreach (var code in deniedCodes.Distinct(StringComparer.Ordinal))
        {
            await roles.AddRolePermissionAsync(new RolePermission
            {
                TenantId = role.TenantId ?? tenantContext.RequireTenantId(),
                BusinessUnitId = role.BusinessUnitId,
                RoleId = role.Id,
                PermissionId = byCode[code].Id,
                PermissionCode = code,
                IsDenied = true,
                GrantedAtUtc = now,
                GrantedByUserId = currentUser.UserId
            }, cancellationToken);
        }

        return Result.Success();
    }

    private async Task ApplyMenuMappingAsync(
        Role role, IReadOnlyList<Guid> visibleMenuIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await menus.GetRoleMenusAsync(role.Id, cancellationToken);
        menus.RemoveRoleMenus(existing);

        foreach (var menuId in visibleMenuIds.Distinct())
        {
            await menus.AddRoleMenuAsync(new RoleMenu
            {
                TenantId = role.TenantId ?? tenantContext.RequireTenantId(),
                BusinessUnitId = role.BusinessUnitId,
                RoleId = role.Id,
                MenuDefinitionId = menuId,
                IsVisible = true,
                MappedAtUtc = now,
                MappedByUserId = currentUser.UserId
            }, cancellationToken);
        }
    }

    /// <summary>At most one default role per Organisation.</summary>
    private async Task ClearOtherDefaultsAsync(Guid tenantId, Guid keepRoleId, CancellationToken cancellationToken)
    {
        var assignable = await roles.GetAssignableAsync(tenantId, cancellationToken);

        foreach (var other in assignable.Where(role => role.IsDefaultRole && role.Id != keepRoleId))
        {
            other.IsDefaultRole = false;
        }
    }

    /// <summary>
    /// Forces every holder of a role to be re-evaluated on their next request.
    ///
    /// Changing a role changes what its holders can do, and an access token already in flight
    /// carries the OLD permission set. Rotating each holder security stamp is what makes the
    /// change immediate rather than a promise that expires with the token.
    /// </summary>
    private async Task InvalidateHoldersAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var members = await roles.GetRoleMembersAsync(roleId, cancellationToken);
        var holderIds = members.Select(assignment => assignment.UserId).Distinct().ToList();

        if (holderIds.Count == 0)
        {
            return;
        }

        var holders = await users.GetManyAsync(holderIds, cancellationToken);

        foreach (var holder in holders)
        {
            holder.SecurityStamp = Guid.NewGuid().ToString("N");
        }
    }
}
