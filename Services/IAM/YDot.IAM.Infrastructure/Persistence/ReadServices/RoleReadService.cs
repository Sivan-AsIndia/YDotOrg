using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Roles.Mappings;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>Read side for the role catalogue and the permission matrix.</summary>
public sealed class RoleReadService(
    IamDbContext context,
    ICurrentUser currentUser) : IRoleReadService
{
    /// <summary>
    /// Drops the PLATFORM roles from an Organisation catalogue.
    ///
    /// The Role query filter carries an "OR TenantId IS NULL" arm — it has to, so a SuperAdmin
    /// keeps their own SUPER_ADMIN grant while operating inside an Organisation — and without
    /// this, that arm would put SUPER_ADMIN into every Organisation's role list, its lookup and
    /// its permission matrix. A TenantAdmin would then be offered a role they cannot be given,
    /// and the refusal would arrive only after they had tried.
    ///
    /// Loading a platform role BY ID still works, which is what the SuperAdmin session needs.
    /// </summary>
    private static IQueryable<Domain.Entities.Role> TenantRolesOnly(
        IQueryable<Domain.Entities.Role> query) =>
        query.Where(role => role.TenantId != null);

    public async Task<PagedResponse<RoleListItemResponse>> SearchAsync(
        RoleSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = TenantRolesOnly(context.Roles.AsNoTracking());

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(role =>
                (role.Name != null && role.Name.ToLower().Contains(term))
                || role.Code.ToLower().Contains(term)
                || (role.Description != null && role.Description.ToLower().Contains(term)));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(role => role.Status == filter.Status.Value);
        }

        if (filter.RoleType.HasValue)
        {
            query = query.Where(role => role.RoleType == filter.RoleType.Value);
        }

        if (filter.IsSystemRole.HasValue)
        {
            query = query.Where(role => role.IsSystemRole == filter.IsSystemRole.Value);
        }

        if (filter.IsPrivileged.HasValue)
        {
            query = query.Where(role => role.IsPrivileged == filter.IsPrivileged.Value);
        }

        // "Which roles can approve payments?" - the question the catalogue screen exists to
        // answer, and the reason this filter is here rather than left to the client.
        if (!string.IsNullOrWhiteSpace(filter.PermissionCode))
        {
            query = query.Where(role =>
                role.GrantsAllTenantPermissions
                || role.RolePermissions.Any(grant =>
                    grant.PermissionCode == filter.PermissionCode && !grant.IsDenied));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(role => new
            {
                Role = role,
                PermissionCount = role.RolePermissions.Count(grant => !grant.IsDenied),
                MemberCount = role.UserRoles.Count(
                    assignment => assignment.Status == UserRoleAssignmentStatus.Active)
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => row.Role.ToListItemResponse(row.PermissionCount, row.MemberCount))
            .ToList();

        return new PagedResponse<RoleListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<RoleDetailResponse?> GetDetailAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .AsNoTracking()
            .Include(item => item.RolePermissions).ThenInclude(grant => grant.Permission)
            .Include(item => item.Claims)
            .Include(item => item.RoleMenus)
            .FirstOrDefaultAsync(item => item.Id == roleId, cancellationToken);

        if (role is null)
        {
            return null;
        }

        var memberCount = await context.UserRoles
            .CountAsync(
                assignment => assignment.RoleId == roleId
                              && assignment.Status == UserRoleAssignmentStatus.Active,
                cancellationToken);

        // Rules naming this role in EITHER direction, because the relationship is symmetric.
        var incompatibilities = await context.RoleIncompatibilities
            .AsNoTracking()
            .Include(rule => rule.Role)
            .Include(rule => rule.ConflictingRole)
            .Where(rule => rule.RoleId == roleId || rule.ConflictingRoleId == roleId)
            .ToListAsync(cancellationToken);

        return role.ToDetailResponse(
            [.. role.RolePermissions.Select(RoleMappingConfig.ToPermissionResponse)],
            [
                .. role.Claims.Select(claim => new RoleClaimResponse(
                    claim.Id, claim.ClaimType ?? string.Empty, claim.ClaimValue ?? string.Empty,
                    claim.Description))
            ],
            [
                .. incompatibilities.Select(rule => new RoleIncompatibilityResponse(
                    rule.Id,
                    rule.RoleId,
                    rule.Role?.Name ?? rule.Role?.Code ?? string.Empty,
                    rule.ConflictingRoleId,
                    rule.ConflictingRole?.Name ?? rule.ConflictingRole?.Code ?? string.Empty,
                    rule.Reason, rule.IsBlocking, rule.IsActive))
            ],
            [.. role.RoleMenus.Where(mapping => mapping.IsVisible).Select(mapping => mapping.MenuDefinitionId)],
            memberCount);
    }

    public async Task<IReadOnlyList<RoleLookupResponse>> LookupAsync(CancellationToken cancellationToken) =>
        await TenantRolesOnly(context.Roles.AsNoTracking())
            .Where(role => role.Status == RoleStatus.Active)
            .OrderByDescending(role => role.Priority)
            .ThenBy(role => role.Name)
            .Select(role => new RoleLookupResponse(
                role.Id,
                role.Code,
                role.Name ?? role.Code,
                role.Status,
                role.IsPrivileged,
                role.IsDefaultRole,
                role.RolePermissions.Count(grant => !grant.IsDenied)))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The permission matrix.
    ///
    /// Grouped by module and then by group, because a flat list of a hundred and thirty codes
    /// is unusable and the whole point of the screen is to let somebody reason about what a
    /// role can do.
    ///
    /// <c>IsImplicitlyGranted</c> marks the codes a blanket-grant role carries without a row.
    /// The checkbox is then shown ticked and disabled, so nobody tries to untick something
    /// that does not exist as a row to remove.
    /// </summary>
    public async Task<PermissionMatrixResponse> GetPermissionMatrixAsync(
        Guid? roleId, CancellationToken cancellationToken)
    {
        // Platform codes are shown only to a root user. Offering them to a TenantAdmin would
        // suggest they could be granted, and the handler would then refuse.
        var includePlatform = currentUser.IsSuperAdmin;

        var permissions = await context.Permissions
            .AsNoTracking()
            .Where(permission => permission.Status == PermissionStatus.Active)
            .Where(permission => includePlatform || !permission.IsPlatformOnly)
            .OrderBy(permission => permission.ModuleCode)
            .ThenBy(permission => permission.GroupCode)
            .ThenBy(permission => permission.DisplayOrder)
            .ToListAsync(cancellationToken);

        Domain.Entities.Role? role = null;
        var granted = new HashSet<Guid>();
        var denied = new HashSet<Guid>();

        if (roleId.HasValue)
        {
            role = await context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == roleId.Value, cancellationToken);

            var grants = await context.RolePermissions
                .AsNoTracking()
                .Where(grant => grant.RoleId == roleId.Value)
                .Select(grant => new { grant.PermissionId, grant.IsDenied })
                .ToListAsync(cancellationToken);

            granted = grants.Where(grant => !grant.IsDenied).Select(grant => grant.PermissionId).ToHashSet();
            denied = grants.Where(grant => grant.IsDenied).Select(grant => grant.PermissionId).ToHashSet();
        }

        var grantsAll = role?.GrantsAllTenantPermissions ?? false;

        var modules = permissions
            .GroupBy(permission => permission.ModuleCode)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(moduleGroup =>
            {
                var groups = moduleGroup
                    .GroupBy(permission => permission.GroupCode ?? string.Empty)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new PermissionMatrixGroupResponse(
                        group.Key,
                        RoleMappingConfig.DescribeGroup(group.Key),
                        [
                            .. group.Select(permission =>
                            {
                                var implicitly = grantsAll && !permission.IsPlatformOnly;
                                var isDenied = denied.Contains(permission.Id);

                                return new PermissionMatrixItemResponse(
                                    permission.Id,
                                    permission.Code,
                                    permission.Name,
                                    permission.Description,
                                    permission.Action,
                                    permission.IsSensitive,
                                    // Deny wins, exactly as it does when access is resolved.
                                    IsGranted: !isDenied && (granted.Contains(permission.Id) || implicitly),
                                    IsDenied: isDenied,
                                    IsImplicitlyGranted: implicitly);
                            })
                        ]))
                    .ToList();

                var moduleGranted = groups.Sum(group => group.Permissions.Count(item => item.IsGranted));

                return new PermissionModuleResponse(
                    moduleGroup.Key,
                    RoleMappingConfig.DescribeModule(moduleGroup.Key),
                    groups,
                    moduleGranted,
                    moduleGroup.Count());
            })
            .ToList();

        var allItems = modules.SelectMany(module => module.Groups).SelectMany(group => group.Permissions).ToList();

        return new PermissionMatrixResponse(
            roleId,
            role?.Name ?? role?.Code,
            grantsAll,
            modules,
            permissions.Count,
            allItems.Count(item => item.IsGranted),
            allItems.Count(item => item.IsGranted && item.IsSensitive));
    }

    public async Task<PagedResponse<PermissionListItemResponse>> SearchPermissionsAsync(
        PermissionSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.Permissions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(permission =>
                permission.Code.ToLower().Contains(term)
                || permission.Name.ToLower().Contains(term)
                || (permission.Description != null && permission.Description.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.ModuleCode))
        {
            query = query.Where(permission => permission.ModuleCode == filter.ModuleCode.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(filter.GroupCode))
        {
            query = query.Where(permission => permission.GroupCode == filter.GroupCode);
        }

        if (filter.Action.HasValue)
        {
            query = query.Where(permission => permission.Action == filter.Action.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(permission => permission.Status == filter.Status.Value);
        }

        if (filter.IsSensitive.HasValue)
        {
            query = query.Where(permission => permission.IsSensitive == filter.IsSensitive.Value);
        }

        // The role editor sets this, because a platform code can never be attached to a Tenant
        // role and listing it would only mislead.
        if (filter.TenantAssignableOnly == true || !currentUser.IsSuperAdmin)
        {
            query = query.Where(permission => !permission.IsPlatformOnly);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(permission => permission.ModuleCode)
            .ThenBy(permission => permission.GroupCode)
            .ThenBy(permission => permission.DisplayOrder)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(permission => new PermissionListItemResponse(
                permission.Id, permission.Code, permission.Name, permission.Description,
                permission.ModuleCode, permission.GroupCode, permission.Action,
                permission.IsSensitive, permission.IsPlatformOnly, permission.Status,
                permission.DisplayOrder))
            .ToListAsync(cancellationToken);

        return new PagedResponse<PermissionListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<PagedResponse<RoleMemberResponse>> GetMembersAsync(
        Guid roleId, PaginationRequest pagination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pagination);

        var now = DateTimeOffset.UtcNow;

        var query = context.UserRoles
            .AsNoTracking()
            .Where(assignment => assignment.RoleId == roleId);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.User!.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(assignment => new
            {
                assignment.Id,
                assignment.UserId,
                UserCode = assignment.User!.Code,
                assignment.User.DisplayName,
                assignment.User.Email,
                UserStatus = assignment.User.Status,
                AssignmentStatus = assignment.Status,
                assignment.IsPrimary,
                assignment.AssignedAtUtc,
                assignment.EffectiveFromUtc,
                assignment.EffectiveToUtc
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new RoleMemberResponse(
                row.Id,
                row.UserId,
                row.UserCode,
                row.DisplayName,
                row.Email ?? string.Empty,
                row.UserStatus,
                row.AssignmentStatus,
                row.IsPrimary,
                row.AssignmentStatus == UserRoleAssignmentStatus.Active
                    && row.EffectiveFromUtc <= now
                    && (row.EffectiveToUtc == null || row.EffectiveToUtc > now),
                row.AssignedAtUtc,
                row.EffectiveToUtc))
            .ToList();

        return new PagedResponse<RoleMemberResponse>(items, total, pagination.Page, pagination.PageSize);
    }

    private static IQueryable<Domain.Entities.Role> ApplySort(
        IQueryable<Domain.Entities.Role> query, string? sort) =>
        (sort?.Trim().ToLowerInvariant()) switch
        {
            "name" or "name asc" => query.OrderBy(role => role.Name),
            "name desc" => query.OrderByDescending(role => role.Name),
            "code" or "code asc" => query.OrderBy(role => role.Code),
            "code desc" => query.OrderByDescending(role => role.Code),
            "priority asc" => query.OrderBy(role => role.Priority),
            "status" => query.OrderBy(role => role.Status).ThenBy(role => role.Name),
            _ => query.OrderByDescending(role => role.Priority).ThenBy(role => role.Name)
        };
}
