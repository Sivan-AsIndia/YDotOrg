using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;
using YDots.CAM.Application.Features.BudgetTargetPlans.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;
using YDots.CAM.Infrastructure.Multitenancy;

namespace YDots.CAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for budget and target plans.
///
/// THE ACTUAL AND THE VARIANCE COME FROM THE DONATIONS, not from a stored figure. A plan's target
/// is what somebody agreed to; what came in against it belongs to payments, and copying that number
/// into CAM would produce a figure that was right when it was written and drifted from that moment.
/// </summary>
public sealed class BudgetTargetPlanReadService(
    CampaignDbContext context,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IFinancialDirectory financial) : IBudgetTargetPlanReadService
{
    public async Task<PagedResponse<BudgetPlanListItemResponse>> SearchAsync(
        BudgetPlanSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var size = filter.PageSize is < 1 or > 200 ? 25 : filter.PageSize;

        var query = ApplyScope(
            context.BudgetTargetPlans
                .AsNoTracking()
                .Include(plan => plan.Versions)
                .Include(plan => plan.Campaign),
            scope);

        query = ApplyFilter(query, filter);

        var total = await query.CountAsync(cancellationToken);

        var plans = await ApplySort(query, filter.Sort)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var items = await ProjectAsync(plans, cancellationToken);

        return new PagedResponse<BudgetPlanListItemResponse>(items, total, page, size);
    }

    public async Task<BudgetPlanDetailResponse?> GetAsync(
        Guid planId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var plan = await ApplyScope(
                context.BudgetTargetPlans
                    .AsNoTracking()
                    .Include(entity => entity.Versions)
                    .Include(entity => entity.Campaign),
                scope)
            .FirstOrDefaultAsync(entity => entity.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var currencyCode = await ResolveCurrencyAsync(plan, cancellationToken);
        var actual = await ActualForAsync(plan, cancellationToken);

        return plan.ToDetailResponse(
            plan.Campaign?.Code ?? string.Empty,
            plan.Campaign?.Name ?? string.Empty,
            currencyCode,
            actual,
            PermittedActionsFor(plan));
    }

    /// <summary>
    /// A campaign's committed budget.
    ///
    /// APPROVED VERSIONS ONLY, one per plan - which the domain guarantees, because the database
    /// permits at most one approved version per plan. Without that guarantee this sum would be the
    /// most dangerous number in the module: a total that looked authoritative and quietly
    /// double-counted every plan that had ever been revised.
    /// </summary>
    public async Task<CampaignBudgetSummaryResponse?> GetCampaignSummaryAsync(
        Guid campaignId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var campaign = await context.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == campaignId, cancellationToken);

        if (campaign is null)
        {
            return null;
        }

        var plans = await context.BudgetTargetPlans
            .AsNoTracking()
            .Include(plan => plan.Versions)
            .Where(plan => plan.CampaignId == campaignId)
            .ToListAsync(cancellationToken);

        var approved = plans
            .Select(plan => plan.ApprovedVersion)
            .Where(version => version is not null)
            .Select(version => version!)
            .ToList();

        var income = await financial.GetCampaignIncomeAsync(
            tenant.TenantId ?? Guid.Empty, campaignId, cancellationToken);

        var committedTarget = approved.Sum(version => version.TargetAmount);
        var variance = income.ConfirmedAmount - committedTarget;

        var currencyIds = approved
            .Select(version => version.CurrencyId)
            .Append(campaign.CurrencyId)
            .Distinct()
            .ToList();

        var codes = await financial.GetCurrencyCodesAsync(currencyIds, cancellationToken);

        return new CampaignBudgetSummaryResponse
        {
            CampaignId = campaign.Id,
            CampaignCode = campaign.Code,
            CommittedTargetAmount = committedTarget,
            CommittedBudgetAmount = approved.Sum(version => version.BudgetAmount),
            CommittedExpectedVolume = approved.Sum(version => version.ExpectedVolume),
            ActualReconciledAmount = income.ConfirmedAmount,
            Variance = variance,
            VariancePercentage = committedTarget == 0m
                ? 0m
                : Math.Round(variance / committedTarget * 100m, 2),
            PlanCount = plans.Count,
            ApprovedPlanCount = approved.Count,

            // What stands between the campaign and a settled budget - the figure somebody chasing
            // approvals actually wants.
            AwaitingApprovalCount = plans.Count(plan =>
                plan.Versions.Any(version => version.ApprovalState == PlanApprovalState.Submitted)),

            CurrencyCode = codes.TryGetValue(campaign.CurrencyId, out var code) ? code : string.Empty
        };
    }

    public async Task<IReadOnlyList<BudgetPlanListItemResponse>> ListForExportAsync(
        BudgetPlanSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyScope(
            context.BudgetTargetPlans
                .AsNoTracking()
                .Include(plan => plan.Versions)
                .Include(plan => plan.Campaign),
            scope);

        query = ApplyFilter(query, filter);

        // CAPPED. An export is a file somebody waits for and then opens in a spreadsheet; an
        // unbounded one is a request that times out and a file nobody can use.
        var plans = await ApplySort(query, filter.Sort)
            .Take(5000)
            .ToListAsync(cancellationToken);

        return await ProjectAsync(plans, cancellationToken);
    }

    // =============================================================================================
    // Internals
    // =============================================================================================

    /// <summary>
    /// Plans with their campaign names, currency codes and actual income filled in.
    ///
    /// THREE LOOKUPS FOR THE WHOLE PAGE, not three per row. The currency codes and the income both
    /// come from outside CAM's own tables, and asking per row would turn a twenty-row page into
    /// forty extra queries.
    /// </summary>
    private async Task<IReadOnlyList<BudgetPlanListItemResponse>> ProjectAsync(
        IReadOnlyList<BudgetTargetPlan> plans, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var currencyIds = plans
            .SelectMany(plan => plan.Versions)
            .Select(version => version.CurrencyId)
            .Distinct()
            .ToList();

        var codes = await financial.GetCurrencyCodesAsync(currencyIds, cancellationToken);

        var campaignIds = plans.Select(plan => plan.CampaignId).Distinct().ToList();

        var income = await financial.GetCampaignIncomeAsync(
            tenant.TenantId ?? Guid.Empty, campaignIds, cancellationToken);

        return plans.Select(plan =>
        {
            var display = plan.ApprovedVersion ?? plan.LatestVersion;

            var currencyCode = display is not null && codes.TryGetValue(display.CurrencyId, out var code)
                ? code
                : string.Empty;

            var actual = income.TryGetValue(plan.CampaignId, out var found) ? found.ConfirmedAmount : 0m;

            return plan.ToListItemResponse(
                plan.Campaign?.Code ?? string.Empty,
                plan.Campaign?.Name ?? string.Empty,
                currencyCode,
                actual,
                PermittedActionsFor(plan));
        }).ToList();
    }

    private async Task<string> ResolveCurrencyAsync(
        BudgetTargetPlan plan, CancellationToken cancellationToken)
    {
        var display = plan.ApprovedVersion ?? plan.LatestVersion;

        if (display is null)
        {
            return string.Empty;
        }

        var codes = await financial.GetCurrencyCodesAsync([display.CurrencyId], cancellationToken);

        return codes.TryGetValue(display.CurrencyId, out var code) ? code : string.Empty;
    }

    private async Task<decimal> ActualForAsync(
        BudgetTargetPlan plan, CancellationToken cancellationToken)
    {
        var income = await financial.GetCampaignIncomeAsync(
            tenant.TenantId ?? Guid.Empty, plan.CampaignId, cancellationToken);

        return income.ConfirmedAmount;
    }

    private static IQueryable<BudgetTargetPlan> ApplyFilter(
        IQueryable<BudgetTargetPlan> query, BudgetPlanSearchFilter filter)
    {
        if (filter.CampaignId is { } campaignId)
        {
            query = query.Where(plan => plan.CampaignId == campaignId);
        }

        if (filter.OwnerUserId is { } ownerId)
        {
            query = query.Where(plan => plan.OwnerUserId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(filter.PlanPeriod))
        {
            var period = filter.PlanPeriod.Trim();
            query = query.Where(plan => plan.PlanPeriod == period);
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetDimension))
        {
            var dimension = filter.TargetDimension.Trim();
            query = query.Where(plan => plan.TargetDimension == dimension);
        }

        if (filter.ApprovalState is { } state)
        {
            query = query.Where(plan => plan.Versions.Any(version => version.ApprovalState == state));
        }

        if (filter.HasApprovedVersion is { } hasApproved)
        {
            query = hasApproved
                ? query.Where(plan =>
                    plan.Versions.Any(version => version.ApprovalState == PlanApprovalState.Approved))
                : query.Where(plan =>
                    !plan.Versions.Any(version => version.ApprovalState == PlanApprovalState.Approved));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();

            query = query.Where(plan =>
                EF.Functions.ILike(plan.Code, $"%{search}%")
                || EF.Functions.ILike(plan.PlanPeriod, $"%{search}%")
                || EF.Functions.ILike(plan.TargetDimension, $"%{search}%")
                || EF.Functions.ILike(plan.Campaign.Code, $"%{search}%")
                || EF.Functions.ILike(plan.Campaign.Name, $"%{search}%"));
        }

        return query;
    }

    /// <summary>
    /// Sorting, from a whitelist.
    ///
    /// A WHITELIST RATHER THAN A COLUMN NAME FROM THE QUERY STRING. Interpolating a caller's string
    /// into an ORDER BY is how a sort parameter becomes an injection point, and an unknown value
    /// falls back to the default rather than failing the request.
    /// </summary>
    private static IQueryable<BudgetTargetPlan> ApplySort(
        IQueryable<BudgetTargetPlan> query, string? sort) => sort?.Trim().ToLowerInvariant() switch
        {
            "code" => query.OrderBy(plan => plan.Code),
            "code_desc" => query.OrderByDescending(plan => plan.Code),
            "period" => query.OrderBy(plan => plan.PlanPeriod).ThenBy(plan => plan.Code),
            "period_desc" => query.OrderByDescending(plan => plan.PlanPeriod).ThenBy(plan => plan.Code),
            "campaign" => query.OrderBy(plan => plan.Campaign.Name).ThenBy(plan => plan.Code),
            "dimension" => query.OrderBy(plan => plan.TargetDimension).ThenBy(plan => plan.Code),
            "created" => query.OrderBy(plan => plan.CreatedAtUtc),
            _ => query.OrderByDescending(plan => plan.CreatedAtUtc).ThenBy(plan => plan.Code)
        };

    /// <summary>
    /// The data scope.
    ///
    /// A CALLER SCOPED TO THEIR OWN RECORDS sees the plans they own or created, AND the plans on
    /// campaigns they own - because a campaign owner needs to see what has been budgeted against
    /// their campaign even when a colleague wrote the plan.
    /// </summary>
    private static IQueryable<BudgetTargetPlan> ApplyScope(
        IQueryable<BudgetTargetPlan> query, AccessScope scope) =>
        scope.IsOwnRecordsOnly
            ? query.Where(plan =>
                plan.OwnerUserId == scope.UserId
                || plan.CreatedByUserId == scope.UserId
                || plan.Campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            : query;

    private IReadOnlyList<string> PermittedActionsFor(BudgetTargetPlan plan)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.BudgetPlansView))
        {
            actions.Add("View");
        }

        var pending = plan.Versions.Any(version =>
            version.ApprovalState is PlanApprovalState.Draft or PlanApprovalState.Submitted);

        if (!pending && currentUser.HasPermission(PermissionCodes.BudgetPlansRevise))
        {
            actions.Add("Revise");
        }

        var draft = plan.Versions.FirstOrDefault(version =>
            version.ApprovalState == PlanApprovalState.Draft);

        if (draft is not null)
        {
            if (currentUser.HasPermission(PermissionCodes.BudgetPlansRevise))
            {
                actions.Add("Edit");
            }

            if (currentUser.HasPermission(PermissionCodes.BudgetPlansSubmit))
            {
                actions.Add("Submit");
            }
        }

        var submitted = plan.Versions.FirstOrDefault(version =>
            version.ApprovalState == PlanApprovalState.Submitted);

        // THE SUBMITTER IS EXCLUDED HERE, exactly as in the handler. A screen that drew an Approve
        // button and then had the click refused would look faulty rather than controlled.
        if (submitted is not null && submitted.SubmittedByUserId != currentUser.UserId)
        {
            if (currentUser.HasPermission(PermissionCodes.BudgetPlansApprove))
            {
                actions.Add("Approve");
            }

            if (currentUser.HasPermission(PermissionCodes.BudgetPlansReject))
            {
                actions.Add("Reject");
            }
        }

        if (currentUser.HasPermission(PermissionCodes.BudgetPlansExport))
        {
            actions.Add("Export");
        }

        return actions;
    }
}
