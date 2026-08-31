using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Common;

/// <summary>
/// The shared base for the five rows of the global master catalogue — Country,
/// StateProvince, City, Currency and TimeZoneDefinition — migrated here from the standalone
/// GlobalMaster service.
///
/// WHY <see cref="ITenantScoped"/> AND NOT <see cref="ITenantOwned"/>. The brief asks for
/// every module to be Tenant-specific, and a naive reading would put a non-null TenantId on
/// these tables. That would mean copying 195 countries, 180 currencies and 400-odd time
/// zones into every Organisation on the platform, and an ISO code would stop meaning one
/// thing across the database the moment two Organisations spelled it differently.
///
/// The scoped filter gives both halves at once:
///
/// <code>
/// TenantId IS NULL   the platform catalogue. Seeded, SuperAdmin-maintained, readable by
///                    every Organisation, and editable by none of them.
/// TenantId = TEN001  a row TEN001 added for itself. Visible ONLY to TEN001, because the
///                    scoped filter is "mine OR platform" and never "somebody else's".
/// </code>
///
/// So an Organisation sees the standard catalogue plus its own additions, cannot see another
/// Organisation's additions, and cannot alter the shared rows. That is real isolation on the
/// half that varies, without duplicating the half that does not.
/// </summary>
public abstract class GlobalMasterEntity : AuditEntity, ITenantScoped, ICodedEntity
{
    /// <summary>Null for a platform row. Set to the owning Organisation for a Tenant addition.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/>, maintained by the DbContext. <c>Guid.Empty</c>
    /// stands for "the platform". Never set it by hand — see <see cref="ITenantScoped"/> for
    /// why the column exists at all.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    /// <summary>
    /// The stable human-readable handle: IN, MH, MUM, INR, ASIA_KOLKATA. Unique inside its
    /// scope, so the platform catalogue and a Tenant's own additions cannot collide with each
    /// other but two Organisations may each define the same private code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The display name shown in lists and pickers.</summary>
    public string Name { get; set; } = string.Empty;

    public MasterDataStatus Status { get; set; } = MasterDataStatus.Active;

    /// <summary>Controls the order rows appear in a picker. Ties fall back to <see cref="Name"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>Free-text working notes. Never shown to a donor.</summary>
    public string? Notes { get; set; }

    /// <summary>True for the seeded catalogue rows that no Organisation may edit or delete.</summary>
    public bool IsPlatformRow => TenantId is null;

    /// <summary>Only an Active row is offered for selection on a form.</summary>
    public bool IsSelectable => Status == MasterDataStatus.Active;
}
