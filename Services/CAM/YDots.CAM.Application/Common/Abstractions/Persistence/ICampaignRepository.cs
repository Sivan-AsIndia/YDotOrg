using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the campaign aggregate.
///
/// WHAT LEFT THIS INTERFACE, AND WHY. It used to carry a <c>SearchAsync</c> that took a
/// <c>GetCampaignsQuery</c> and returned a page of <c>CampaignListResponse</c>, which made the
/// persistence contract depend on a query type and a response DTO from the features folder -
/// the dependency pointing the wrong way through the layers. The grid projections moved to
/// <see cref="ICampaignReadService"/>, and what is left here is the aggregate loading that a
/// COMMAND needs.
///
/// It also carried its own <c>SaveChangesAsync</c>. Committing is the unit of work, not the
/// repository: with a save on each repository, a handler that touched a campaign and an audit
/// row had two commits and no way to make them one.
///
/// EVERY READ HERE PASSES THROUGH THE ORGANISATION QUERY FILTER, so none of these methods can
/// return another Organisation's campaign - which is why none of them takes a TenantId.
/// </summary>
public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken);

    /// <summary>
    /// One campaign with its owners and channels loaded, for editing.
    ///
    /// Returns null when it does not exist OR belongs to another Organisation. The filter makes
    /// those two indistinguishable, which is the point: a caller probing for ids learns nothing
    /// from the difference.
    /// </summary>
    Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a campaign code is already taken inside the caller's Organisation.
    ///
    /// <paramref name="excludeCampaignId"/> is the row being edited, so a rename that keeps its
    /// own code does not collide with itself.
    /// </summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeCampaignId, CancellationToken cancellationToken);

    /// <summary>
    /// The outstanding close request, if any.
    ///
    /// A campaign may have many lifecycle actions but only one PENDING close request at a time;
    /// that invariant is what lets the approve-close handler act on "the" request without
    /// asking which one.
    /// </summary>
    Task<CampaignLifecycleAction?> GetPendingCloseRequestAsync(
        Guid campaignId, CancellationToken cancellationToken);

    Task AddLifecycleActionAsync(CampaignLifecycleAction action, CancellationToken cancellationToken);

    /// <summary>Required readiness checks that have not passed. Empty means the campaign may launch.</summary>
    Task<IReadOnlyList<CampaignReadinessCheck>> GetOutstandingRequiredChecksAsync(
        Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Tracking assets attached to a campaign. Non-empty blocks deleting a draft.</summary>
    Task<int> CountTrackingAssetsAsync(Guid campaignId, CancellationToken cancellationToken);

    void Delete(Campaign campaign);
}
