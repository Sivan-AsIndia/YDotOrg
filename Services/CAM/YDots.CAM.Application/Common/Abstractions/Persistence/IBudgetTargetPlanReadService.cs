using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>Read-side projections for budget and target plans.</summary>
public interface IBudgetTargetPlanReadService
{
    Task<PagedResponse<BudgetPlanListItemResponse>> SearchAsync(
        BudgetPlanSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    Task<BudgetPlanDetailResponse?> GetAsync(
        Guid planId, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// A campaign's committed budget, summed from the approved version of each of its plans.
    ///
    /// NOT PAGED and not filterable. It answers one question - what is this campaign committed to -
    /// and a partial answer to that question is worse than none, because it looks like a total.
    /// </summary>
    Task<CampaignBudgetSummaryResponse?> GetCampaignSummaryAsync(
        Guid campaignId, AccessScope scope, CancellationToken cancellationToken);

    /// <summary>The register as a CSV, respecting the same filter and scope as the grid.</summary>
    Task<IReadOnlyList<BudgetPlanListItemResponse>> ListForExportAsync(
        BudgetPlanSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);
}
