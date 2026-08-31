namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Named authorization policies. Permission policies are created on demand by the permission
/// policy provider using the prefix below, so only the fixed policies are listed here.
/// </summary>
public static class PolicyNames
{
    /// <summary>Prefix used by the dynamic permission policy provider, for example Permission:don.donors.create.</summary>
    public const string PermissionPrefix = "Permission:";

    /// <summary>Claim policy: the caller must be an active user.</summary>
    public const string ActiveUserOnly = "ActiveUserOnly";

    /// <summary>Data scope: the caller must carry an organisation claim before any query runs.</summary>
    public const string SameOrganisation = "SameOrganisation";

    /// <summary>Segregation of duties: an approver may never approve a record they created.</summary>
    public const string IndependentApprover = "IndependentApprover";

    /// <summary>Builds the dynamic policy name for a permission code.</summary>
    public static string ForPermission(string permissionCode) => PermissionPrefix + permissionCode;
}
