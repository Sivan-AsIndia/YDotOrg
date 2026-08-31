using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to roles, their permissions and their assignments.</summary>
public sealed class RoleRepository(IamDbContext context) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Roles.FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    public Task<Role?> GetWithPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Roles
            .Include(role => role.RolePermissions).ThenInclude(grant => grant.Permission)
            .Include(role => role.RoleMenus)
            .Include(role => role.Claims)
            .FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    /// <summary>
    /// A role by its code inside one Organisation.
    ///
    /// Filters bypassed and the Organisation named explicitly, because the seeder and the
    /// platform paths look roles up in Organisations they have not selected into.
    /// </summary>
    public Task<Role?> GetByCodeAsync(string code, Guid? tenantId, CancellationToken cancellationToken)
    {
        var normalised = code.ToUpperInvariant();

        return context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                role => role.TenantId == tenantId && role.NormalizedCode == normalised,
                cancellationToken);
    }

    public Task<Role?> GetDefaultRoleAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                role => role.TenantId == tenantId && role.IsDefaultRole && role.Status == RoleStatus.Active,
                cancellationToken);

    public Task<bool> CodeExistsAsync(
        string normalizedCode, Guid? tenantId, Guid? excludingRoleId, CancellationToken cancellationToken) =>
        context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId)
            .Where(role => excludingRoleId == null || role.Id != excludingRoleId)
            .AnyAsync(role => role.NormalizedCode == normalizedCode.ToUpperInvariant(), cancellationToken);

    public Task<bool> NameExistsAsync(
        string normalizedName, Guid? tenantId, Guid? excludingRoleId, CancellationToken cancellationToken) =>
        context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId)
            .Where(role => excludingRoleId == null || role.Id != excludingRoleId)
            .AnyAsync(role => role.NormalizedName == normalizedName.ToUpperInvariant(), cancellationToken);

    public async Task<IReadOnlyList<Role>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Roles
            .Where(role => ids.Contains(role.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> GetAssignableAsync(
        Guid? tenantId, CancellationToken cancellationToken) =>
        await context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId && role.Status == RoleStatus.Active)
            .OrderByDescending(role => role.Priority)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Role role, CancellationToken cancellationToken) =>
        await context.Roles.AddAsync(role, cancellationToken);

    public void Remove(Role role) => context.Roles.Remove(role);

    /// <summary>How many people currently hold this role. Only LIVE assignments count.</summary>
    public Task<int> CountAssignmentsAsync(Guid roleId, CancellationToken cancellationToken) =>
        context.UserRoles.CountAsync(
            assignment => assignment.RoleId == roleId
                          && assignment.Status == UserRoleAssignmentStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(
        Guid roleId, CancellationToken cancellationToken) =>
        await context.RolePermissions
            .Include(grant => grant.Permission)
            .Where(grant => grant.RoleId == roleId)
            .ToListAsync(cancellationToken);

    public async Task AddRolePermissionAsync(RolePermission rolePermission, CancellationToken cancellationToken) =>
        await context.RolePermissions.AddAsync(rolePermission, cancellationToken);

    public void RemoveRolePermissions(IEnumerable<RolePermission> rolePermissions) =>
        context.RolePermissions.RemoveRange(rolePermissions);

    /// <summary>
    /// Every assignment a user has ever held, live or closed.
    ///
    /// The closed ones are included deliberately: an access review needs to see what somebody
    /// used to hold, and the caller filters with <c>IsEffective</c> when it wants only the live set.
    /// </summary>
    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.UserRoles
            .Include(assignment => assignment.Role)
            .Where(assignment => assignment.UserId == userId)
            .OrderByDescending(assignment => assignment.AssignedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A user's assignments inside one named Organisation, with the ambient filter off.
    ///
    /// The Organisation comes from the user record the caller already loaded, never from
    /// anything a caller sent, and it is re-applied here explicitly so a reviewer can see it.
    /// </summary>
    public async Task<IReadOnlyList<UserRole>> GetUserRolesInTenantAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken) =>
        await context.UserRoles
            .IgnoreQueryFilters()
            .Include(assignment => assignment.Role)
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => tenantId.HasValue
                ? assignment.TenantId == tenantId.Value
                : assignment.TenantId == null)
            .OrderByDescending(assignment => assignment.AssignedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<UserRole?> GetActiveAssignmentAsync(
        Guid userId, Guid roleId, CancellationToken cancellationToken) =>
        context.UserRoles.FirstOrDefaultAsync(
            assignment => assignment.UserId == userId
                          && assignment.RoleId == roleId
                          && assignment.Status == UserRoleAssignmentStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<UserRole>> GetRoleMembersAsync(
        Guid roleId, CancellationToken cancellationToken) =>
        await context.UserRoles
            .Include(assignment => assignment.User)
            .Where(assignment => assignment.RoleId == roleId
                                 && assignment.Status == UserRoleAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken) =>
        await context.UserRoles.AddAsync(userRole, cancellationToken);

    /// <summary>
    /// Rules naming any of the given roles, IN EITHER DIRECTION.
    ///
    /// The symmetry matters: the rule is stored once as (A, B), but "may A and B be held
    /// together?" has to find it whichever way round the caller asks. Missing one direction
    /// would let the conflict be created simply by assigning the roles in the other order.
    /// </summary>
    public async Task<IReadOnlyList<RoleIncompatibility>> GetIncompatibilitiesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await context.RoleIncompatibilities
            .Include(rule => rule.Role)
            .Include(rule => rule.ConflictingRole)
            .Where(rule => rule.IsActive)
            .Where(rule => roleIds.Contains(rule.RoleId) || roleIds.Contains(rule.ConflictingRoleId))
            .ToListAsync(cancellationToken);
    }

    public async Task AddIncompatibilityAsync(
        RoleIncompatibility incompatibility, CancellationToken cancellationToken) =>
        await context.RoleIncompatibilities.AddAsync(incompatibility, cancellationToken);

    public void RemoveIncompatibility(RoleIncompatibility incompatibility) =>
        context.RoleIncompatibilities.Remove(incompatibility);

    public Task<RoleIncompatibility?> GetIncompatibilityAsync(Guid id, CancellationToken cancellationToken) =>
        context.RoleIncompatibilities
            .Include(rule => rule.Role)
            .Include(rule => rule.ConflictingRole)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RoleClaimEntry>> GetRoleClaimsAsync(
        Guid roleId, CancellationToken cancellationToken) =>
        await context.RoleClaims
            .Where(claim => claim.RoleId == roleId)
            .ToListAsync(cancellationToken);

    public async Task AddRoleClaimAsync(RoleClaimEntry claim, CancellationToken cancellationToken) =>
        await context.RoleClaims.AddAsync(claim, cancellationToken);

    public void RemoveRoleClaims(IEnumerable<RoleClaimEntry> claims) =>
        context.RoleClaims.RemoveRange(claims);
}

/// <summary>
/// The global permission catalogue.
///
/// Read-mostly and NOT Tenant-filtered, because it is genuinely global — every Organisation
/// draws from the same set. Writes come from the seeder rather than a screen.
/// </summary>
public sealed class PermissionRepository(IamDbContext context) : IPermissionRepository
{
    public Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Permissions.FirstOrDefaultAsync(permission => permission.Id == id, cancellationToken);

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Permissions.FirstOrDefaultAsync(permission => permission.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken) =>
        await context.Permissions
            .OrderBy(permission => permission.ModuleCode)
            .ThenBy(permission => permission.GroupCode)
            .ThenBy(permission => permission.DisplayOrder)
            .ThenBy(permission => permission.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Permission>> GetByCodesAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (codes.Count == 0)
        {
            return [];
        }

        return await context.Permissions
            .Where(permission => codes.Contains(permission.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Permission>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Permissions
            .Where(permission => ids.Contains(permission.Id))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Everything a Tenant role is allowed to carry.
    ///
    /// Active, and NOT platform-only. This single query is what stops a role edit ever handing
    /// an Organisation the ability to create or approve Organisations — the platform codes are
    /// simply never in the set a role can be built from.
    /// </summary>
    public async Task<IReadOnlyList<Permission>> GetTenantAssignableAsync(CancellationToken cancellationToken) =>
        await context.Permissions
            .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
            .OrderBy(permission => permission.ModuleCode)
            .ThenBy(permission => permission.GroupCode)
            .ThenBy(permission => permission.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Permission>> GetByModuleAsync(
        string moduleCode, CancellationToken cancellationToken) =>
        await context.Permissions
            .Where(permission => permission.ModuleCode == moduleCode.ToUpperInvariant())
            .OrderBy(permission => permission.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Permission permission, CancellationToken cancellationToken) =>
        await context.Permissions.AddAsync(permission, cancellationToken);

    /// <summary>
    /// The effective permission codes for one user: the union of every active role, plus
    /// direct claims, minus anything explicitly denied.
    ///
    /// Written as two projections and a set operation rather than loading the aggregates,
    /// because this runs on the sign-in path and only the strings are wanted.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var grants = await context.UserRoles
            .Where(assignment => assignment.UserId == userId
                                 && assignment.Status == UserRoleAssignmentStatus.Active
                                 && assignment.EffectiveFromUtc <= now
                                 && (assignment.EffectiveToUtc == null || assignment.EffectiveToUtc > now))
            .Join(
                context.Roles.Where(role => role.Status == RoleStatus.Active),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => role.Id)
            .Join(
                context.RolePermissions,
                roleId => roleId,
                grant => grant.RoleId,
                (roleId, grant) => new { grant.PermissionCode, grant.IsDenied, grant.ExpiresAtUtc })
            .ToListAsync(cancellationToken);

        var allowed = grants
            .Where(grant => !grant.IsDenied && (grant.ExpiresAtUtc == null || grant.ExpiresAtUtc > now))
            .Select(grant => grant.PermissionCode)
            .ToHashSet(StringComparer.Ordinal);

        // Direct user claims are added on top of the role grants.
        var direct = await context.UserClaims
            .Where(claim => claim.UserId == userId
                            && claim.ClaimType == ClaimTypeNames.Permission
                            && (claim.ExpiresAtUtc == null || claim.ExpiresAtUtc > now))
            .Select(claim => claim.ClaimValue)
            .ToListAsync(cancellationToken);

        foreach (var code in direct.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            allowed.Add(code!);
        }

        // Deny is applied LAST and beats every allow, which is what lets one permission be
        // carved out of a broad role without unpicking the role.
        foreach (var denied in grants.Where(grant => grant.IsDenied).Select(grant => grant.PermissionCode))
        {
            allowed.Remove(denied);
        }

        return allowed;
    }
}
