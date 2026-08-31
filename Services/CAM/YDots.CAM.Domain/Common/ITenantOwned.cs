namespace YDots.CAM.Domain.Common;

/// <summary>
/// THE ISOLATION MARKER. Every entity that belongs to exactly one Organisation implements
/// this, and it is the only thing <c>CampaignDbContext</c> looks at when it decides whether to
/// attach a global query filter.
///
/// Implementing this interface has three automatic consequences, none of which a handler has
/// to remember:
///
///   1. The DbContext attaches a global query filter, so a read can never see another
///      Organisation's row even if somebody forgets a Where clause.
///   2. <c>SaveChangesAsync</c> stamps TenantId and BusinessUnitId from the request's
///      <c>ITenantContext</c> on insert, so a caller cannot choose them, and refuses to move
///      an existing row between Organisations.
///   3. The EF configuration includes TenantId in every unique constraint, so two
///      Organisations may each run a campaign coded SUMMER25 but neither may run two.
///
/// WHY THIS REPLACES THE BARE <c>OrganisationId</c> COLUMN. The property was already on
/// Campaign, TrackingAsset and the readiness tables, but it was only ever a column: isolation
/// depended on every single repository method remembering to filter by it, and on every
/// insert remembering to set it. One forgotten Where clause was one Organisation reading
/// another's campaigns, and nothing in the type system would have said so. The marker turns
/// that from a convention into a property of the model.
///
/// Do NOT implement it on the genuinely global reference tables - Channel, Source, Medium -
/// where an Organisation boundary has no business meaning.
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning Organisation. Displayed as "Organisation" in the UI.</summary>
    Guid TenantId { get; set; }

    /// <summary>
    /// The root boundary above Tenant. Denormalised so a BusinessUnit-wide report never has to
    /// join through IAM to reach it.
    /// </summary>
    Guid BusinessUnitId { get; set; }
}
