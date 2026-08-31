namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Where a role lives. This is what makes "Roles should be Tenant-specific" enforceable.
///
/// A <see cref="Platform"/> role has <c>TenantId == null</c> and belongs to nobody — only
/// SuperAdmin holds one. A <see cref="Tenant"/> role belongs to exactly one Organisation,
/// and two Organisations may both have a role coded ADMIN without any relationship
/// between them. A <see cref="Template"/> role is a BusinessUnit-level blueprint that is
/// copied into a new Tenant at creation time; editing the template never rewrites the
/// copies, which is what stops a platform change silently altering a customer's access.
/// </summary>
public enum RoleType
{
    /// <summary>Tenant-scoped. The normal case.</summary>
    Tenant = 0,

    /// <summary>Global/platform role. TenantId is null. SuperAdmin only.</summary>
    Platform = 1,

    /// <summary>A BusinessUnit blueprint copied into each new Tenant.</summary>
    Template = 2
}
