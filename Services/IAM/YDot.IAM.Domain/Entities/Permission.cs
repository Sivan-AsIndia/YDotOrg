using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One fine-grained capability, for example <c>iam.users.create</c>.
///
/// DELIBERATELY GLOBAL. This table has no TenantId, and that is not an oversight — it is
/// section 46 of the brief: "do not automatically add TenantId to genuinely global tables
/// where it has no business meaning". A permission is a fact about what the software can
/// do, not about who may do it. Every Organisation draws from the same catalogue; what
/// differs per Organisation is which permissions its roles carry, and that lives in
/// <see cref="RolePermission"/>, which is Tenant-owned.
///
/// If this table were per-Tenant, adding a module would mean inserting the same rows into
/// every Organisation and every future one, and the permission codes that DON and the other
/// services compile against would stop being a single stable contract.
///
/// THE CODES ARE A CROSS-SERVICE CONTRACT. A sibling service enforces
/// <c>don.donors.create</c> by looking for a claim of that exact string in the token IAM
/// signed. Codes are append-only; retire rather than rename.
/// </summary>
public class Permission : AuditEntity, ICodedEntity
{
    /// <summary>
    /// The dotted key that is written into the token and compiled into the other services:
    /// <c>iam.users.create</c>. Globally unique, max 100.
    ///
    /// This doubles as the mandatory master Code column.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The section abbreviation that owns it: IAM, DON, CAM, GM.</summary>
    public string ModuleCode { get; set; } = string.Empty;

    /// <summary>Second-level grouping inside the module, used to lay out the permission matrix.</summary>
    public string? GroupCode { get; set; }

    public PermissionAction Action { get; set; } = PermissionAction.View;

    /// <summary>
    /// True when exercising this permission has to be recorded with enhanced audit detail.
    /// Approvals, exports and anything that unmasks personal data are sensitive.
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// True when only SuperAdmin may ever hold it, regardless of role mapping. This is what
    /// protects the platform-level operations — creating an Organisation, approving one —
    /// from being granted to a TenantAdmin by an over-enthusiastic role edit.
    /// </summary>
    public bool IsPlatformOnly { get; set; }

    public PermissionStatus Status { get; set; } = PermissionStatus.Active;

    /// <summary>Ordering within the group on the permission matrix screen.</summary>
    public int DisplayOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    /// <summary>True when the permission may still be assigned. Retired codes stay in the
    /// table so historical audit rows continue to resolve to a readable name.</summary>
    public bool IsAssignable => Status == PermissionStatus.Active;
}
