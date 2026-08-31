namespace YDots.CAM.Domain.Common;

/// <summary>
/// Every master table carries a unique <c>Code</c>: the stable human-readable handle that
/// seed data, imports and integrations refer to, so a row can be found again without knowing
/// its generated Guid.
///
/// Uniqueness is SCOPED, not global. The reference tables - Channel, Source, Medium - are
/// unique platform-wide because their codes appear in tracking URLs that outlive any one
/// Organisation. Tenant-owned rows such as Campaign are unique inside their Organisation, so
/// two Organisations may each run SUMMER25 without collision. The EF configuration for each
/// table decides which.
/// </summary>
public interface ICodedEntity
{
    string Code { get; set; }
}
