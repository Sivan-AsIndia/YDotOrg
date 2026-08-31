using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to the tracking asset aggregate.</summary>
public sealed class TrackingAssetRepository(CampaignDbContext context) : ITrackingAssetRepository
{
    public async Task AddAsync(TrackingAsset trackingAsset, CancellationToken cancellationToken) =>
        await context.TrackingAssets.AddAsync(trackingAsset, cancellationToken);

    /// <summary>One asset with its placements and their custom fields, tracked for editing.</summary>
    public Task<TrackingAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.TrackingAssets
            .Include(asset => asset.Places)
                .ThenInclude(place => place.CustomFields)
            .FirstOrDefaultAsync(asset => asset.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code, Guid? excludeAssetId, CancellationToken cancellationToken) =>
        context.TrackingAssets
            .Where(asset => asset.Code == code)
            .Where(asset => excludeAssetId == null || asset.Id != excludeAssetId)
            .AnyAsync(cancellationToken);

    public Task<int> CountForCampaignAsync(Guid campaignId, CancellationToken cancellationToken) =>
        context.TrackingAssets.CountAsync(asset => asset.CampaignId == campaignId, cancellationToken);

    /// <summary>
    /// Whether a tracking reference is in use anywhere on the platform.
    ///
    /// THE ONE PLACE IN THIS MODULE THAT BYPASSES THE ORGANISATION FILTER, and it has to. A
    /// reference arrives from the public donation flow with no session and no Organisation, so
    /// it is resolved globally - which means a collision between two Organisations would credit
    /// one Organisation's gift to another. Checking only inside the caller's own Organisation
    /// would not see that collision at all.
    ///
    /// It returns a BOOLEAN and never the row, so nothing about another Organisation's asset
    /// escapes through it.
    /// </summary>
    public Task<bool> TrackingReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        context.TrackingAssets
            .IgnoreQueryFilters()
            .AnyAsync(asset => asset.TrackingReference == reference, cancellationToken);
}
