using Microsoft.Extensions.Logging;
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
    IUnitOfWork unitOfWork,
    ILogger<BudgetPlanQueryHandler> logger)
{
    public async Task<Result<PagedResponse<BudgetPlanListItemResponse>>> HandleAsync(
        SearchBudgetPlansQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching budget plans. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page, query.Filter.PageSize);

        var page = await readService.SearchAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation("Budget plan search completed. TotalCount: {TotalCount}.",
            page.TotalCount);

        return Result.Success(page);
    }

    public async Task<Result<BudgetPlanDetailResponse>> HandleAsync(
        GetBudgetPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogDebug("Retrieving budget plan detail. PlanId: {PlanId}.",
            query.PlanId);

        var plan = await readService.GetAsync(
            query.PlanId, currentUser.Scope, cancellationToken);

        if (plan is null)
        {
            logger.LogWarning("Budget plan not found. PlanId: {PlanId}.",
                query.PlanId);

            return Result.Failure<BudgetPlanDetailResponse>(
                Error.NotFound("That budget plan was not found."));
        }

        logger.LogInformation("Budget plan detail retrieved. PlanId: {PlanId}.",
            query.PlanId);

        return Result.Success(plan);
    }

    public async Task<Result<CampaignBudgetSummaryResponse>> HandleAsync(
        GetCampaignBudgetSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign budget summary. CampaignId: {CampaignId}.",
            query.CampaignId);

        var summary = await readService.GetCampaignSummaryAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        if (summary is null)
        {
            logger.LogWarning("Campaign budget summary not found. CampaignId: {CampaignId}.",
                query.CampaignId);

            return Result.Failure<CampaignBudgetSummaryResponse>(
                Error.NotFound("That campaign was not found."));
        }

        logger.LogInformation("Campaign budget summary retrieved. CampaignId: {CampaignId}.",
            query.CampaignId);

        return Result.Success(summary);
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

        logger.LogInformation("Starting budget plan export.");

        var rows = await readService.ListForExportAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation("Budget plan export rows retrieved. RowCount: {RowCount}.",
            rows.Count);

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

        logger.LogInformation("Budget plan export completed. RowCount: {RowCount}, Reference: {Reference}.",
            rows.Count, file.Reference);

        return Result.Success(file);
    }
}