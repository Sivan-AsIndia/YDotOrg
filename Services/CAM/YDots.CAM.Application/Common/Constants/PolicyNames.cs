namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The named authorization policies, plus the convention that turns any permission code into a
/// policy on demand.
///
/// Permission policies are NOT registered one by one. <c>PermissionPolicyProvider</c>
/// manufactures them from the prefix below, so decorating an endpoint with
/// <c>[HasPermission("cam.campaigns.approve")]</c> is all that is ever needed - which is what
/// keeps forty-odd permission codes from becoming forty lines of startup configuration.
/// </summary>
public static class PolicyNames
{
    /// <summary>Marks a policy name as "this is really a permission check".</summary>
    public const string PermissionPrefix = "Permission:";

    public static string ForPermission(string permissionCode) => PermissionPrefix + permissionCode;

    /// <summary>Caller must be an Active user. Blocks a suspended account holding a live token.</summary>
    public const string ActiveUserOnly = "ActiveUserOnly";

    /// <summary>Caller must carry a resolved Organisation context.</summary>
    public const string TenantContextRequired = "TenantContextRequired";

    /// <summary>Caller must be the platform root user.</summary>
    public const string SuperAdminOnly = "SuperAdminOnly";
}
