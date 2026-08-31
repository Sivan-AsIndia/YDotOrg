namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The named authorization policies, plus the convention that turns any permission code into
/// a policy on demand.
///
/// Permission policies are NOT registered one by one. <c>PermissionPolicyProvider</c>
/// manufactures them from the prefix below, so decorating an endpoint with
/// <c>[HasPermission("iam.users.create")]</c> is all that is ever needed. That is what keeps
/// eighty-odd permission codes from becoming eighty lines of startup configuration.
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

    /// <summary>Caller must administer the Organisation they are operating in, or be SuperAdmin.</summary>
    public const string TenantAdminOnly = "TenantAdminOnly";

    /// <summary>Caller must have satisfied their second factor in this session.</summary>
    public const string MfaCompleted = "MfaCompleted";

    /// <summary>Caller must have re-authenticated recently. Guards the sensitive actions.</summary>
    public const string RecentlyReauthenticated = "RecentlyReauthenticated";

    /// <summary>Caller may not act on a record whose subject is themselves.</summary>
    public const string IndependentApprover = "IndependentApprover";

    /// <summary>The token must be a full access token, not an MFA-pending or step-up one.</summary>
    public const string FullAccessToken = "FullAccessToken";
}
