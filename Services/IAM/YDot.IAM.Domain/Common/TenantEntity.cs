namespace YDot.IAM.Domain.Common;

/// <summary>
/// Convenience base for the common case: an audited entity owned by one Tenant.
/// Equivalent to writing <c>AuditEntity</c> plus <see cref="ITenantOwned"/> by hand.
/// </summary>
public abstract class TenantEntity : AuditEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid BusinessUnitId { get; set; }
}
