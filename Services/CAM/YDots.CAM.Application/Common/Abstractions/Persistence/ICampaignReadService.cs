using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.Campaigns.DTOs;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read-side projections for the campaign register and the campaign detail screen.
///
/// SEPARATE FROM <see cref="ICampaignRepository"/> for the usual reason: a repository loads a
/// tracked aggregate so it can be changed, while a grid wants a dozen columns for twenty rows.
/// Loading twenty tracked Campaign aggregates with their owners, channels, lifecycle actions
/// and readiness checks in order to draw a list is how a screen ends up issuing forty queries
/// and holding a change-tracker full of entities nobody intends to modify.
///
/// The Organisation filter is applied underneath by the DbContext, so nothing here has to
/// remember it and nothing here can reach past it. <see cref="AccessScope"/> only ever NARROWS
/// within one Organisation - to the caller's own campaigns, for a role scoped that way.
/// </summary>
public interface ICampaignReadService
{
    Task<PagedResponse<CampaignListItemResponse>> SearchAsync(
        CampaignSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    Task<CampaignDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>The audit trail for one campaign, newest first.</summary>
    Task<PagedResponse<CampaignHistoryResponse>> GetHistoryAsync(
        Guid campaignId, PaginationRequest pagination, CancellationToken cancellationToken);

    /// <summary>Counts by status, for the register's summary tiles.</summary>
    Task<CampaignStatisticsResponse> GetStatisticsAsync(
        AccessScope scope, CancellationToken cancellationToken);

    /// <summary>The rows behind a CSV export, already scoped.</summary>
    Task<IReadOnlyList<CampaignExportRow>> GetExportRowsAsync(
        CampaignSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>Active campaigns for a picker, so a tracking asset can name its campaign.</summary>
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? search, int take, CancellationToken cancellationToken);
}
