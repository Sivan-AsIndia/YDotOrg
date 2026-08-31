namespace YDots.CAM.Domain.Common;

/// <summary>
/// Convenience base for the common case: an audited entity owned by one Organisation.
/// Equivalent to writing <see cref="AuditEntity"/> plus <see cref="ITenantOwned"/> by hand.
/// </summary>
public abstract class TenantEntity : AuditEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid BusinessUnitId { get; set; }
}
