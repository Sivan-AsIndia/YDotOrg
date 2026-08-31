using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// The root platform entity: www.ngoplanet.com.
///
/// A BusinessUnit is the outermost boundary in the model. It owns N Organisations/Tenants,
/// it owns the global permission catalogue and the global menu catalogue, and it is the
/// only thing that sits above a Tenant. Nothing has a global query filter applied against
/// it, because there is nothing above it to filter by.
///
/// The platform ships with exactly one of these today, but it is a table rather than a
/// configuration constant so a second BusinessUnit can be introduced without a migration
/// of every foreign key in the schema.
/// </summary>
public class BusinessUnit : AuditEntity, ICodedEntity
{
    /// <summary>Unique platform-wide handle, for example BU001.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    /// <summary>
    /// The apex domain that every Tenant subdomain hangs off: "ngoplanet.com".
    /// A Tenant with subdomain "ten1" is therefore reachable at ten1.ngoplanet.com.
    /// Stored without a scheme and without a leading dot.
    /// </summary>
    public string RootDomain { get; set; } = string.Empty;

    public BusinessUnitStatus Status { get; set; } = BusinessUnitStatus.Active;

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? SupportEmail { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>IANA zone, for example "Asia/Kolkata". New Tenants inherit this as their default.</summary>
    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>ISO 4217, for example "INR".</summary>
    public string DefaultCurrency { get; set; } = "INR";

    /// <summary>BCP 47, for example "en-IN".</summary>
    public string DefaultCulture { get; set; } = "en-IN";

    /// <summary>
    /// Ceiling on how many Organisations may exist under this BusinessUnit.
    /// Null means no ceiling, which is the default.
    /// </summary>
    public int? MaximumTenants { get; set; }

    public string? Description { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = [];

    /// <summary>True when the BusinessUnit itself may be operated in.</summary>
    public bool IsOperable => Status == BusinessUnitStatus.Active;
}
