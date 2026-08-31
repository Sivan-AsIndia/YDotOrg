using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to budget and target plans.
///
/// Every read here returns ENTITIES, not response DTOs. The shaped, joined projections the register
/// needs live in <see cref="IBudgetTargetPlanReadService"/>, so the persistence contract does not
/// end up depending on the features folder with the dependency pointing the wrong way through the
/// layers.
/// </summary>
public interface IBudgetTargetPlanRepository
{
    Task AddAsync(BudgetTargetPlan plan, CancellationToken cancellationToken);

    /// <summary>One plan with its full version history loaded, for editing and deciding.</summary>
    Task<BudgetTargetPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One version, loaded with the plan it belongs to and that plan's other versions.</summary>
    Task<BudgetTargetPlanVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken);

    Task AddVersionAsync(BudgetTargetPlanVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a plan already covers this campaign, period and dimension.
    ///
    /// The database enforces this too, with a unique index. This exists so the screen gets a clear
    /// "a plan already covers that" instead of a constraint violation - the index is what makes the
    /// rule hold under two simultaneous requests, and this is what makes it readable.
    /// </summary>
    Task<BudgetTargetPlan?> FindDuplicateAsync(
        Guid campaignId,
        string planPeriod,
        string targetDimension,
        Guid? excludePlanId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The next plan reference for the caller's organisation.
    ///
    /// GAPS ARE ACCEPTABLE HERE, unlike a receipt number: a plan reference identifies a plan, it is
    /// not a legal sequence. What matters is that it is unique, which the unique index guarantees
    /// even if two callers race for the same number.
    /// </summary>
    Task<string> NextCodeAsync(CancellationToken cancellationToken);

    /// <summary>The approved versions across a campaign's plans - what its committed budget is made of.</summary>
    Task<IReadOnlyList<BudgetTargetPlanVersion>> GetApprovedVersionsForCampaignAsync(
        Guid campaignId, CancellationToken cancellationToken);
}
