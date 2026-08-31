using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.Attribution.DTOs;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>Read-side projections for the attribution explorer.</summary>
public interface IAttributionReadService
{
    Task<PagedResponse<AttributionListItemResponse>> SearchAsync(
        AttributionSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    Task<AttributionDetailResponse?> GetAsync(
        Guid donationId, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// How income breaks down by channel, source, medium and asset.
    ///
    /// Scoped to one campaign when an id is given, and to the whole organisation otherwise - the
    /// two questions people ask are "how is this campaign doing?" and "which channels work for us?".
    /// </summary>
    Task<AttributionSummaryResponse> GetSummaryAsync(
        Guid? campaignId, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>The explorer as a CSV, respecting the same filter and scope as the grid.</summary>
    Task<IReadOnlyList<AttributionListItemResponse>> ListForExportAsync(
        AttributionSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);
}

/// <summary>Write-side access to attribution correction requests.</summary>
public interface IAttributionCorrectionRepository
{
    Task AddAsync(
        Domain.Entities.AttributionCorrectionRequest request, CancellationToken cancellationToken);

    Task<Domain.Entities.AttributionCorrectionRequest?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken);

    /// <summary>The open request against a donation, if there is one.</summary>
    Task<Domain.Entities.AttributionCorrectionRequest?> GetOpenForDonationAsync(
        Guid donationId, CancellationToken cancellationToken);

    /// <summary>
    /// Which of these donations already have an open request.
    ///
    /// TAKES A SET, because the explorer flags every row on a page and asking per row would turn
    /// one page into twenty-five extra queries.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetDonationsWithOpenRequestsAsync(
        IReadOnlyCollection<Guid> donationIds, CancellationToken cancellationToken);
}
