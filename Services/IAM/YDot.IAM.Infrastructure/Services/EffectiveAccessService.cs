using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Resolves what a user can actually do, from every source at once.
///
/// THE PRECEDENCE, IN ORDER, and it matters that it is in one place:
///
/// <code>
/// 1. SuperAdmin                    everything, full stop
/// 2. GrantsAllTenantPermissions    every Tenant-assignable code in the catalogue
/// 3. explicit deny                 beats any allow below it
/// 4. role permissions              union of every ACTIVE, in-window assignment
/// 5. direct user claims            added on top
/// </code>
///
/// Deny beating allow is what lets one permission be carved out of a broad role without
/// unpicking the role. Applying it LAST is what makes that true regardless of the order the
/// rows happen to come back in.
///
/// WHY THIS IS ONE SERVICE RATHER THAN FOUR QUERIES SCATTERED AROUND. The token builder and
/// the IAM-USR-03 preview screen both need this answer. Two implementations would drift, and
/// the day they drift is the day the screen tells an administrator something different from
/// what the token actually grants.
/// </summary>
public sealed class EffectiveAccessService(
    IamDbContext context,
    IDateTimeProvider clock) : IEffectiveAccessService
{
    public async Task<EffectiveAccess> ResolveAsync(
        Guid userId, Guid? operatingTenantId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        return user is null
            ? EffectiveAccess.None(userId)
            : await ResolveAsync(user, operatingTenantId, cancellationToken);
    }

    public async Task<EffectiveAccess> ResolveAsync(
        User user, Guid? operatingTenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;

        // ---- 1. SuperAdmin -------------------------------------------------------------
        //
        // Everything, and no rows are read to establish it. Their token deliberately carries
        // no permission claims either: the scope claim is the grant, and writing a hundred and
        // thirty claims into every token would bloat every request for no benefit.
        if (user.IsSuperAdmin)
        {
            var platformRoles = await context.Roles
                .IgnoreQueryFilters()
                .Where(role => role.RoleType == RoleType.Platform)
                .Select(role => new EffectiveRole(
                    role.Id, role.Code, role.Name ?? role.Code, true, true, null, 0))
                .ToListAsync(cancellationToken);

            return new EffectiveAccess(
                user.Id,
                operatingTenantId,
                user.BusinessUnitId,
                IsSuperAdmin: true,
                HasAllTenantPermissions: true,
                platformRoles,
                // Empty by design. HasPermission returns true unconditionally for a root user,
                // so the set is never consulted.
                new HashSet<string>(StringComparer.Ordinal),
                [],
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        // ---- The live assignments -------------------------------------------------------------
        var assignments = await context.UserRoles
            .IgnoreQueryFilters()
            .Include(assignment => assignment.Role)
            .Where(assignment => assignment.UserId == user.Id)
            .Where(assignment => assignment.Status == UserRoleAssignmentStatus.Active)
            .Where(assignment => assignment.EffectiveFromUtc <= now)
            .Where(assignment => assignment.EffectiveToUtc == null || assignment.EffectiveToUtc > now)
            .ToListAsync(cancellationToken);

        // An INACTIVE role stops granting immediately, without the assignment having to be
        // revoked. That is what makes "deactivate this role" take effect at once.
        var liveRoles = assignments
            .Where(assignment => assignment.Role is not null && assignment.Role.Status == RoleStatus.Active)
            .ToList();

        var roleIds = liveRoles.Select(assignment => assignment.RoleId).Distinct().ToList();

        // ---- 2. The blanket grant -----------------------------------------------------------------
        var hasAllTenantPermissions = liveRoles.Any(
            assignment => assignment.Role!.GrantsAllTenantPermissions);

        var permissionCodes = new HashSet<string>(StringComparer.Ordinal);

        if (hasAllTenantPermissions)
        {
            // Everything a Tenant role is ALLOWED to carry - which excludes the platform-only
            // codes. That exclusion is what stops a TenantAdmin approving Organisations.
            var assignable = await context.Permissions
                .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
                .Select(permission => permission.Code)
                .ToListAsync(cancellationToken);

            foreach (var code in assignable)
            {
                permissionCodes.Add(code);
            }
        }

        // ---- 3 and 4. Role permissions, with deny applied last --------------------------------------
        var grants = roleIds.Count == 0
            ? []
            : await context.RolePermissions
                .IgnoreQueryFilters()
                .Where(grant => roleIds.Contains(grant.RoleId))
                .Where(grant => grant.ExpiresAtUtc == null || grant.ExpiresAtUtc > now)
                .Select(grant => new { grant.PermissionCode, grant.IsDenied, grant.RoleId })
                .ToListAsync(cancellationToken);

        foreach (var grant in grants.Where(item => !item.IsDenied))
        {
            permissionCodes.Add(grant.PermissionCode);
        }

        // ---- 5. Direct user claims ----------------------------------------------------------------------
        var claims = await context.UserClaims
            .IgnoreQueryFilters()
            .Where(claim => claim.UserId == user.Id)
            .Where(claim => claim.ExpiresAtUtc == null || claim.ExpiresAtUtc > now)
            .Select(claim => new { claim.ClaimType, claim.ClaimValue })
            .ToListAsync(cancellationToken);

        foreach (var claim in claims.Where(item =>
                     item.ClaimType == ClaimTypeNames.Permission && !string.IsNullOrWhiteSpace(item.ClaimValue)))
        {
            permissionCodes.Add(claim.ClaimValue!);
        }

        // DENY IS APPLIED LAST, so it beats a role grant, the blanket grant and a direct claim
        // alike. Anything else would make the outcome depend on evaluation order.
        foreach (var denied in grants.Where(item => item.IsDenied).Select(item => item.PermissionCode))
        {
            permissionCodes.Remove(denied);
        }

        // ---- Data scopes ---------------------------------------------------------------------------------
        var dataScopes = await context.UserDataScopes
            .IgnoreQueryFilters()
            .Where(scope => scope.UserId == user.Id)
            .Where(scope => scope.RevokedAtUtc == null)
            .Where(scope => scope.EffectiveFromUtc <= now)
            .Where(scope => scope.EffectiveToUtc == null || scope.EffectiveToUtc > now)
            .Select(scope => scope.ScopeType.ToString() + ":" + scope.ScopeValue)
            .ToListAsync(cancellationToken);

        // ---- Role claims, which travel in the token alongside the permissions -------------------------------
        var roleClaims = roleIds.Count == 0
            ? []
            : await context.RoleClaims
                .IgnoreQueryFilters()
                .Where(claim => roleIds.Contains(claim.RoleId))
                .Select(claim => new { claim.ClaimType, claim.ClaimValue })
                .ToListAsync(cancellationToken);

        var claimDictionary = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var claim in roleClaims.Where(item =>
                     !string.IsNullOrWhiteSpace(item.ClaimType) && !string.IsNullOrWhiteSpace(item.ClaimValue))
                 .Select(item => new { ClaimType = item.ClaimType!, item.ClaimValue }))
        {
            claimDictionary[claim.ClaimType] = claim.ClaimValue!;
        }

        foreach (var claim in claims.Where(item =>
                     item.ClaimType != ClaimTypeNames.Permission
                     && !string.IsNullOrWhiteSpace(item.ClaimType)
                     && !string.IsNullOrWhiteSpace(item.ClaimValue))
                 .Select(item => new { ClaimType = item.ClaimType!, item.ClaimValue }))
        {
            // A direct claim overrides a role claim of the same type: it is the more specific
            // statement about this one person.
            claimDictionary[claim.ClaimType] = claim.ClaimValue!;
        }

        // ---- The role summaries the preview screen renders ---------------------------------------------------
        var permissionCountByRole = grants
            .Where(grant => !grant.IsDenied)
            .GroupBy(grant => grant.RoleId)
            .ToDictionary(group => group.Key, group => group.Count());

        var effectiveRoles = liveRoles
            .Select(assignment => new EffectiveRole(
                assignment.RoleId,
                assignment.Role!.Code,
                assignment.Role.Name ?? assignment.Role.Code,
                assignment.IsPrimary,
                assignment.Role.GrantsAllTenantPermissions,
                assignment.EffectiveToUtc,
                permissionCountByRole.GetValueOrDefault(assignment.RoleId)))
            .OrderByDescending(role => role.IsPrimary)
            .ThenBy(role => role.Name, StringComparer.Ordinal)
            .ToList();

        return new EffectiveAccess(
            user.Id,
            operatingTenantId ?? user.TenantId,
            user.BusinessUnitId,
            IsSuperAdmin: false,
            hasAllTenantPermissions,
            effectiveRoles,
            permissionCodes,
            dataScopes,
            claimDictionary);
    }

    /// <summary>
    /// What the access WOULD become if these roles replaced the current ones.
    ///
    /// The comparison is computed from the same resolution logic as the real thing, so the
    /// preview cannot promise something the commit would not deliver.
    /// </summary>
    public async Task<AccessComparison> PreviewAsync(
        Guid userId,
        Guid? operatingTenantId,
        IReadOnlyCollection<Guid> proposedRoleIds,
        CancellationToken cancellationToken)
    {
        var current = await ResolveAsync(userId, operatingTenantId, cancellationToken);

        var now = clock.UtcNow;

        var proposedRoles = proposedRoleIds.Count == 0
            ? []
            : await context.Roles
                .IgnoreQueryFilters()
                .Where(role => proposedRoleIds.Contains(role.Id) && role.Status == RoleStatus.Active)
                .ToListAsync(cancellationToken);

        var after = new HashSet<string>(StringComparer.Ordinal);

        if (proposedRoles.Any(role => role.GrantsAllTenantPermissions))
        {
            var assignable = await context.Permissions
                .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
                .Select(permission => permission.Code)
                .ToListAsync(cancellationToken);

            foreach (var code in assignable)
            {
                after.Add(code);
            }
        }

        var proposedGrants = proposedRoleIds.Count == 0
            ? []
            : await context.RolePermissions
                .IgnoreQueryFilters()
                .Where(grant => proposedRoleIds.Contains(grant.RoleId))
                .Where(grant => grant.ExpiresAtUtc == null || grant.ExpiresAtUtc > now)
                .Select(grant => new { grant.PermissionCode, grant.IsDenied })
                .ToListAsync(cancellationToken);

        foreach (var grant in proposedGrants.Where(item => !item.IsDenied))
        {
            after.Add(grant.PermissionCode);
        }

        // Direct claims survive a role change - they were granted to the person, not the role.
        var directClaims = await context.UserClaims
            .IgnoreQueryFilters()
            .Where(claim => claim.UserId == userId && claim.ClaimType == ClaimTypeNames.Permission)
            .Where(claim => claim.ExpiresAtUtc == null || claim.ExpiresAtUtc > now)
            .Select(claim => claim.ClaimValue)
            .ToListAsync(cancellationToken);

        foreach (var code in directClaims.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            after.Add(code!);
        }

        foreach (var denied in proposedGrants.Where(item => item.IsDenied).Select(item => item.PermissionCode))
        {
            after.Remove(denied);
        }

        return AccessComparison.Between(current.PermissionCodes, after, PermissionCodes.IsSensitive);
    }

    /// <summary>
    /// Checks a proposed role set against the segregation-of-duties rules.
    ///
    /// Two kinds of conflict are looked for, and both matter: within the PROPOSED set, and
    /// between the proposed set and what the person ALREADY holds. Checking only the first
    /// would let the forbidden pair be assembled one role at a time across two saves, which is
    /// exactly how it happens in practice.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckSegregationOfDutiesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> proposedRoleIds,
        CancellationToken cancellationToken)
    {
        if (proposedRoleIds.Count == 0)
        {
            return [];
        }

        var now = clock.UtcNow;

        var existingRoleIds = await context.UserRoles
            .IgnoreQueryFilters()
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => assignment.Status == UserRoleAssignmentStatus.Active)
            .Where(assignment => assignment.EffectiveToUtc == null || assignment.EffectiveToUtc > now)
            .Select(assignment => assignment.RoleId)
            .ToListAsync(cancellationToken);

        // The union is what gets checked: the end state after the change, not the delta.
        var combined = proposedRoleIds.Concat(existingRoleIds).Distinct().ToList();

        var rules = await context.RoleIncompatibilities
            .IgnoreQueryFilters()
            .Include(rule => rule.Role)
            .Include(rule => rule.ConflictingRole)
            .Where(rule => rule.IsActive && rule.IsBlocking)
            .Where(rule => combined.Contains(rule.RoleId) && combined.Contains(rule.ConflictingRoleId))
            .ToListAsync(cancellationToken);

        return
        [
            .. rules.Select(rule =>
                $"{rule.Role?.Name ?? rule.Role?.Code ?? "A role"} and "
                + $"{rule.ConflictingRole?.Name ?? rule.ConflictingRole?.Code ?? "another role"} "
                + $"cannot be held together: {rule.Reason}")
        ];
    }
}
