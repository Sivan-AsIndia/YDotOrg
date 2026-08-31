using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the tracking asset aggregate.
///
/// The grid projection moved to <see cref="ITrackingAssetReadService"/>, and the
/// <c>SaveChangesAsync</c> that used to sit here moved to <see cref="IUnitOfWork"/> - see
/// <see cref="ICampaignRepository"/> for why both of those were wrong where they were.
///
/// Every read passes through the Organisation query filter, so none of these can return
/// another Organisation's asset.
/// </summary>
public interface ITrackingAssetRepository
{
    Task AddAsync(TrackingAsset trackingAsset, CancellationToken cancellationToken);

    /// <summary>One asset with its placements and their custom fields loaded, for editing.</summary>
    Task<TrackingAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether an asset code is taken inside the caller's Organisation.</summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeAssetId, CancellationToken cancellationToken);

    /// <summary>
    /// How many assets already exist for a campaign.
    ///
    /// Used to number a generated code - CAMP01-QR-003 - so an operator can tell one QR code
    /// from another in a list without reading the destination.
    /// </summary>
    Task<int> CountForCampaignAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a tracking reference is already in use, ACROSS EVERY ORGANISATION.
    ///
    /// The one deliberate exception to the filter in this interface, and it has to be. A
    /// reference arrives from the public donation flow with no session and no Organisation, so
    /// it is resolved globally - which means a collision between two Organisations would credit
    /// one Organisation's gift to another. Checking only inside the caller's own Organisation
    /// would not see that collision at all.
    /// </summary>
    Task<bool> TrackingReferenceExistsAsync(string reference, CancellationToken cancellationToken);
}
