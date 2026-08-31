using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A claim carried by every holder of a role. Used for the facts that are not permissions but
/// still belong in the token — an approval limit, a cost-centre code.
///
/// A CUSTOMISED IdentityCore TABLE, deriving from <see cref="IdentityRoleClaim{TKey}"/>, so
/// <c>Id</c>, <c>RoleId</c>, <c>ClaimType</c> and <c>ClaimValue</c> come from the base and
/// RoleManager.AddClaimAsync writes through this entity.
/// </summary>
public class RoleClaimEntry : IdentityRoleClaim<Guid>, ITenantScoped
{
    /// <summary>
    /// Nullable so the row can belong to the global SuperAdmin or a Platform role, which
    /// belong to no Organisation. Matches the nullability of User.TenantId and Role.TenantId.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    public Role? Role { get; set; }

    public string? Description { get; set; }
}
