namespace YDot.IAM.Domain.Common;

/// <summary>
/// Every master table carries a unique <c>Code</c>, whether or not the specification
/// mentioned one. A Code is the stable human-readable handle that seed data, imports and
/// integrations refer to, so a row can be found again without knowing its generated Guid.
///
/// Uniqueness is scoped: global masters are unique platform-wide, Tenant-owned masters are
/// unique inside their Tenant. The EF configuration for each table decides which.
/// </summary>
public interface ICodedEntity
{
    string Code { get; set; }
}
