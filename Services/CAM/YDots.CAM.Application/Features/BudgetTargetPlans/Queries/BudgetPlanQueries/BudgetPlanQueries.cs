using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;

namespace YDots.CAM.Application.Features.BudgetTargetPlans.Queries.BudgetPlanQueries;

/// <summary>One page of the plan register.</summary>
public sealed record SearchBudgetPlansQuery(BudgetPlanSearchFilter Filter);

/// <summary>One plan with its full version history.</summary>
public sealed record GetBudgetPlanQuery(Guid PlanId);

/// <summary>A campaign's committed budget, summed from the approved version of each plan.</summary>
public sealed record GetCampaignBudgetSummaryQuery(Guid CampaignId);

/// <summary>The register as a CSV.</summary>
public sealed record ExportBudgetPlansQuery(BudgetPlanSearchFilter Filter);

/// <summary>The read side of the Budget and Target Plans slice.</summary>
public sealed class BudgetPlanQueryHandler(
    IBudgetTargetPlanReadService readService,
    ICsvExportService csv,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<PagedResponse<BudgetPlanListItemResponse>>> HandleAsync(
        SearchBudgetPlansQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await readService.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);

        return Result.Success(page);
    }

    public async Task<Result<BudgetPlanDetailResponse>> HandleAsync(
        GetBudgetPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var plan = await readService.GetAsync(query.PlanId, currentUser.Scope, cancellationToken);

        return plan is null
            ? Result.Failure<BudgetPlanDetailResponse>(
                Error.NotFound("That budget plan was not found."))
            : Result.Success(plan);
    }

    public async Task<Result<CampaignBudgetSummaryResponse>> HandleAsync(
        GetCampaignBudgetSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var summary = await readService.GetCampaignSummaryAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        return summary is null
            ? Result.Failure<CampaignBudgetSummaryResponse>(
                Error.NotFound("That campaign was not found."))
            : Result.Success(summary);
    }

    /// <summary>
    /// The register as a CSV.
    ///
    /// AUDITED, unlike a grid load. A budget export carries an organisation's targets and spend in a
    /// file that outlives the session, so who took it and when is worth being able to answer.
    ///
    /// IT EXPORTS THE FILTERED SET, not everything. An export that quietly widened the filter would
    /// hand somebody more than the screen in front of them showed.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportBudgetPlansQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await readService.ListForExportAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        var file = csv.ToCsv(rows.Select(row => new
        {
            Plan = row.Code,
            Campaign = row.CampaignCode,
            CampaignName = row.CampaignName,
            Period = row.PlanPeriod,
            Dimension = row.TargetDimension,
            Version = row.DisplayVersion?.VersionLabel ?? string.Empty,
            State = row.DisplayVersion?.ApprovalState.ToString() ?? string.Empty,
            Currency = row.DisplayVersion?.CurrencyCode ?? string.Empty,
            Target = row.DisplayVersion?.TargetAmount ?? 0m,
            Budget = row.DisplayVersion?.BudgetAmount ?? 0m,
            Category = row.DisplayVersion?.BudgetCategory ?? string.Empty,
            ExpectedVolume = row.DisplayVersion?.ExpectedVolume ?? 0,
            Actual = row.DisplayVersion?.ActualReconciledAmount ?? 0m,
            Variance = row.DisplayVersion?.Variance ?? 0m,
            InForce = row.HasApprovedVersion
        }).ToList(), "budget-target-plans");

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Exported, nameof(BudgetPlanListItemResponse), Guid.Empty,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }
}
