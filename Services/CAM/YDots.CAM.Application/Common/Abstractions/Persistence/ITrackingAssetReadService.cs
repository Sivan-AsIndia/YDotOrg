using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read-side projections for the tracking asset manager.
///
/// The names it returns - campaign, channel, source, medium - are joined IN the projection
/// rather than fetched per row afterwards. A grid of twenty assets showing four names each is
/// one query this way and eighty-one the other.
/// </summary>
public interface ITrackingAssetReadService
{
    Task<PagedResponse<TrackingAssetListItemResponse>> SearchAsync(
        TrackingAssetSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    Task<TrackingAssetDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackingAssetExportRow>> GetExportRowsAsync(
        TrackingAssetSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);
}
