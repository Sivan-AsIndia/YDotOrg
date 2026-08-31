namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// Everything a user can actually do, resolved from every source at once. This is what the
/// IAM-USR-03 "Permission and data-scope preview" screen renders, and what the token builder
/// reads.
///
/// WHY THIS TYPE EXISTS. Access arrives from four places: roles, explicit user claims,
/// data scopes, and the blanket grant a Tenant-wide or SuperAdmin flag confers. An
/// administrator looking at four separate screens cannot reliably answer "so what can this
/// person actually see?", which is exactly the question that matters before an audit.
/// Resolving it once, in one place, means the preview screen and the token can never
/// disagree.
/// </summary>
public sealed record EffectiveAccess(
    Guid UserId,
    Guid? TenantId,
    Guid BusinessUnitId,
    bool IsSuperAdmin,
    bool HasAllTenantPermissions,
    IReadOnlyList<EffectiveRole> Roles,
    IReadOnlySet<string> PermissionCodes,
    IReadOnlyList<string> DataScopes,
    IReadOnlyDictionary<string, string> Claims)
{
    public bool HasPermission(string permissionCode) =>
        HasAllTenantPermissions || IsSuperAdmin || PermissionCodes.Contains(permissionCode);

    public static EffectiveAccess None(Guid userId) =>
        new(userId, null, Guid.Empty, false, false, [],
            new HashSet<string>(StringComparer.Ordinal), [],
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>One role the user holds, with the reason it counts, for the preview screen.</summary>
public sealed record EffectiveRole(
    Guid RoleId,
    string Code,
    string Name,
    bool IsPrimary,
    bool GrantsAllTenantPermissions,
    DateTimeOffset? EffectiveToUtc,
    int PermissionCount);
