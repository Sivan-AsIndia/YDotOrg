namespace YDot.IAM.Domain.Common;

/// <summary>
/// THE ISOLATION MARKER. Every entity that belongs to exactly one Organisation/Tenant
/// implements this, and that is the only thing the DbContext looks at when it decides
/// whether to attach a global query filter.
///
/// Implementing this interface has three automatic consequences, none of which a handler
/// has to remember:
///
///   1. <c>IamDbContext</c> attaches a global query filter, so a read can never see another
///      Tenant's row even if somebody forgets a Where clause.
///   2. <c>IamDbContext.SaveChangesAsync</c> stamps TenantId and BusinessUnitId from the
///      request's <c>ITenantContext</c> on insert, so a caller cannot choose them.
///   3. The EF configuration adds a Tenant-scoped index and includes TenantId in every
///      unique constraint, so the same e-mail may exist in two Tenants but never twice
///      inside one.
///
/// Do NOT implement this on genuinely global tables (BusinessUnit, Tenant, Permission,
/// the global menu catalogue). Section 46 of the brief: "Do not automatically add TenantId
/// to genuinely global tables where it has no business meaning."
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning Organisation. Displayed as "Organisation" in the UI.</summary>
    Guid TenantId { get; set; }

    /// <summary>The root boundary above Tenant. Denormalised so a BusinessUnit-wide
    /// report never has to join through Tenant.</summary>
    Guid BusinessUnitId { get; set; }
}
