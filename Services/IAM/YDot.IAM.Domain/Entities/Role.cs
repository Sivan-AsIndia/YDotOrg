using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A named bundle of permissions. Roles are Tenant-specific, exactly as the brief requires,
/// and one user may hold several of them.
///
/// A CUSTOMISED IdentityCore TABLE, like <see cref="User"/>. It derives from
/// <see cref="IdentityRole{TKey}"/> with a Guid key, so <c>Name</c>,
/// <c>NormalizedName</c>, <c>Id</c> and <c>ConcurrencyStamp</c> come from the base and are
/// not redeclared here. RoleManager therefore works against this entity unchanged.
///
/// TENANT-SPECIFIC MEANS GENUINELY SEPARATE. Two Organisations can both have a role coded
/// ADMIN with nothing to do with one another — different rows, different permission sets, no
/// shared lifecycle. IdentityCore normally puts a UNIQUE index on <c>NormalizedName</c>,
/// which would make that impossible, so the EF configuration replaces it with a composite
/// index on (TenantId, NormalizedName). Same customisation, same reason, as on
/// <see cref="User"/>.
///
/// THE NULL TENANT IS THE PLATFORM ROLE. A role with <see cref="TenantId"/> = null is a
/// platform role held by SuperAdmin. It exists so the root user has somewhere to hang their
/// identity without being enrolled into an Organisation.
///
/// SUPERADMIN DOES NOT NEED ONE PER TENANT. Section 4.1: SuperAdmin can reach every Tenant
/// module "without needing to be individually assigned every Tenant permission". That is
/// handled at authorisation time by the Global scope claim, not by copying roles into every
/// Organisation.
/// </summary>
public class Role : IdentityRole<Guid>, IAuditable, ITenantScoped, ICodedEntity
{
    public Role()
    {
        Id = Guid.NewGuid();
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    // ---- Audit, from IAuditable. Stamped by the DbContext. --------------------------------

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public long Version { get; set; } = 1;

    // ---- Tenancy ----------------------------------------------------------------------------

    /// <summary>Null only for a platform or template role.</summary>
    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    // ---- Role definition -----------------------------------------------------------------------

    /// <summary>
    /// Upper-case, max 50, unique inside the Tenant. For example CAMPAIGN_MANAGER.
    ///
    /// Distinct from the inherited <c>Name</c>: Code is the stable machine handle that seed
    /// data and integrations refer to, Name is the label a person reads and may rename.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string NormalizedCode { get; set; } = string.Empty;

    public string? Description { get; set; }

    public RoleType RoleType { get; set; } = RoleType.Tenant;

    public RoleStatus Status { get; set; } = RoleStatus.Draft;

    /// <summary>
    /// System roles are seeded, cannot be deleted, and cannot have their code changed. Their
    /// permission set can still be adjusted, because an Organisation may legitimately decide
    /// its own administrator should not, say, export.
    /// </summary>
    public bool IsSystemRole { get; set; }

    /// <summary>The role new users get when none is chosen. At most one per Tenant.</summary>
    public bool IsDefaultRole { get; set; }

    /// <summary>
    /// Grants everything inside the Organisation without listing each permission. This is the
    /// TenantAdmin role, and it is why a new module does not require every customer to re-map
    /// their administrator.
    ///
    /// Emphatically NOT a way out of the Tenant: the query filter and the token tenant_id
    /// still apply, so a holder sees all of one Organisation and none of any other.
    /// </summary>
    public bool GrantsAllTenantPermissions { get; set; }

    /// <summary>Ordering hint, and the tie-breaker when two roles disagree. Higher wins.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// True when holding this role means the person can approve things they cannot also
    /// create. Used by the segregation-of-duties checks.
    /// </summary>
    public bool IsPrivileged { get; set; }

    /// <summary>Colour or icon token used by the UI badge. Purely cosmetic.</summary>
    public string? DisplayTag { get; set; }

    // ---- Navigations -------------------------------------------------------------------------------

    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    public ICollection<UserRole> UserRoles { get; set; } = [];

    public ICollection<RoleMenu> RoleMenus { get; set; } = [];

    public ICollection<RoleClaimEntry> Claims { get; set; } = [];

    // ---- Derived state -------------------------------------------------------------------------------

    /// <summary>True when the role may currently be assigned to somebody.</summary>
    public bool IsAssignable => Status == RoleStatus.Active;

    /// <summary>A platform role belongs to no Organisation and only SuperAdmin holds one.</summary>
    public bool IsPlatformRole => RoleType == RoleType.Platform && TenantId is null;
}
