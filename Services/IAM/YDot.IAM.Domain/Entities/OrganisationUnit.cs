using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A branch, region or office inside one Organisation.
///
/// Distinct from <see cref="Department"/> on purpose: a department is what you do, a unit is
/// where you are. Somebody can be in Finance (department) at the Chennai office (unit), and
/// reporting almost always needs to slice by both independently.
/// </summary>
public class OrganisationUnit : TenantEntity, ICodedEntity
{
    /// <summary>Unique inside the Tenant, for example CHN.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Units nest: country contains state contains branch.</summary>
    public Guid? ParentUnitId { get; set; }

    public OrganisationUnit? Parent { get; set; }

    public ICollection<OrganisationUnit> Children { get; set; } = [];

    /// <summary>Free-text classification: Head Office, Regional Office, Branch, Warehouse.</summary>
    public string? UnitType { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Country { get; set; }

    public string? PostalCode { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    /// <summary>Overrides the Organisation zone for people based here.</summary>
    public string? TimeZone { get; set; }

    public Guid? ManagerUserId { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public int DisplayOrder { get; set; }

    public ICollection<User> Users { get; set; } = [];

    public bool IsAssignable => Status == RecordStatus.Active;
}
