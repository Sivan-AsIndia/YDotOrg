using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to budget and target plans.</summary>
public sealed class BudgetTargetPlanRepository(CampaignDbContext context) : IBudgetTargetPlanRepository
{
    public async Task AddAsync(BudgetTargetPlan plan, CancellationToken cancellationToken) =>
        await context.BudgetTargetPlans.AddAsync(plan, cancellationToken);

    /// <summary>
    /// One plan with its full version history, tracked for editing.
    ///
    /// THE VERSIONS ARE ALWAYS INCLUDED because every write path asks about them - is anything
    /// still pending, which version is approved, what number comes next. Loading them separately
    /// would turn one query into two on every path and, worse, would let a handler reason about a
    /// plan whose versions it had not seen.
    /// </summary>
    public Task<BudgetTargetPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.BudgetTargetPlans
            .Include(plan => plan.Versions)
            .FirstOrDefaultAsync(plan => plan.Id == id, cancellationToken);

    /// <summary>
    /// One version, with its plan and that plan's OTHER versions.
    ///
    /// The siblings matter: approving a version has to find the one it supersedes, and that
    /// decision cannot be made from the version in isolation.
    /// </summary>
    public Task<BudgetTargetPlanVersion?> GetVersionAsync(
        Guid versionId, CancellationToken cancellationToken) =>
        context.BudgetTargetPlanVersions
            .Include(version => version.Plan)
                .ThenInclude(plan => plan.Versions)
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);

    public async Task AddVersionAsync(
        BudgetTargetPlanVersion version, CancellationToken cancellationToken) =>
        await context.BudgetTargetPlanVersions.AddAsync(version, cancellationToken);

    public Task<BudgetTargetPlan?> FindDuplicateAsync(
        Guid campaignId,
        string planPeriod,
        string targetDimension,
        Guid? excludePlanId,
        CancellationToken cancellationToken)
    {
        var period = planPeriod.Trim();
        var dimension = targetDimension.Trim();

        var query = context.BudgetTargetPlans
            .AsNoTracking()
            .Where(plan => plan.CampaignId == campaignId)

            // CASE-INSENSITIVE, because "Q3" and "q3" are the same period to everybody except a
            // string comparison, and two plans differing only in capitalisation is exactly the
            // duplicate this is meant to catch.
            .Where(plan => plan.PlanPeriod.ToLower() == period.ToLower())
            .Where(plan => plan.TargetDimension.ToLower() == dimension.ToLower());

        if (excludePlanId is { } exclude)
        {
            query = query.Where(plan => plan.Id != exclude);
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The next plan reference for this organisation.
    ///
    /// DERIVED FROM THE HIGHEST EXISTING NUMBER rather than from a count, because a count would
    /// reissue a number as soon as anything was ever removed - and a reissued plan reference means
    /// two different plans answering to the same code in somebody's correspondence.
    ///
    /// The Organisation filter is applied by the DbContext, so this counts only the caller's own
    /// plans and two organisations can each hold BTP-2026-0001.
    /// </summary>
    public async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var prefix = $"BTP-{year}-";

        var lastCode = await context.BudgetTargetPlans
            .AsNoTracking()
            .Where(plan => plan.Code.StartsWith(prefix))
            .OrderByDescending(plan => plan.Code)
            .Select(plan => plan.Code)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;

        if (!string.IsNullOrEmpty(lastCode)
            && int.TryParse(lastCode[prefix.Length..], out var lastNumber))
        {
            next = lastNumber + 1;
        }

        return $"{prefix}{next:D4}";
    }

    public async Task<IReadOnlyList<BudgetTargetPlanVersion>> GetApprovedVersionsForCampaignAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        await context.BudgetTargetPlanVersions
            .AsNoTracking()
            .Include(version => version.Plan)
            .Where(version => version.Plan.CampaignId == campaignId)
            .Where(version => version.ApprovalState == PlanApprovalState.Approved)
            .ToListAsync(cancellationToken);
}
