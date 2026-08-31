namespace YDot.IAM.Domain.Common;

/// <summary>
/// For the handful of rows that sit between "global" and "one Tenant": they belong to a
/// BusinessUnit but span every Tenant underneath it. <c>Tenant</c> itself is the obvious
/// example — it has a BusinessUnitId but no TenantId, because it *is* the Tenant.
///
/// These get no global query filter. A BusinessUnit is the outermost boundary, and only
/// SuperAdmin reaches across it.
/// </summary>
public interface IBusinessUnitOwned
{
    Guid BusinessUnitId { get; set; }
}
