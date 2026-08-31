using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;

// =================================================================================================
// Requests
// =================================================================================================

/// <summary>
/// Allocates a new plan.
///
/// NO CODE FIELD. The plan reference is minted by the server, because two people allocating at the
/// same moment would otherwise be free to mint the same one - and a plan reference is what a
/// finance team quotes.
/// </summary>
public sealed record AllocateBudgetPlanRequest
{
    public Guid CampaignId { get; init; }

    public string PlanPeriod { get; init; } = string.Empty;

    public string TargetDimension { get; init; } = string.Empty;

    public Guid OwnerUserId { get; init; }

    public decimal TargetAmount { get; init; }

    public decimal BudgetAmount { get; init; }

    /// <summary>
    /// The currency the two amounts are in.
    ///
    /// OPTIONAL, DEFAULTING TO THE CAMPAIGN'S. A plan in a different currency from its campaign is
    /// legitimate - a regional plan in local currency against a campaign reported centrally - but
    /// it is rare enough that requiring it on every request would mostly produce mistakes.
    /// </summary>
    public Guid? CurrencyId { get; init; }

    public string BudgetCategory { get; init; } = string.Empty;

    public int ExpectedVolume { get; init; }

    public string? Assumptions { get; init; }
}

/// <summary>
/// Revises a plan into a NEW version.
///
/// IT DOES NOT EDIT THE EXISTING ONE. That is the whole point of revising: the figures somebody
/// approved stay exactly as they were approved, and the new ones sit alongside them.
/// </summary>
public sealed record ReviseBudgetPlanRequest
{
    /// <summary>The plan version this revision was written against.</summary>
    public long ExpectedVersion { get; init; }

    public decimal TargetAmount { get; init; }

    public decimal BudgetAmount { get; init; }

    public Guid? CurrencyId { get; init; }

    public string BudgetCategory { get; init; } = string.Empty;

    public int ExpectedVolume { get; init; }

    public string? Assumptions { get; init; }

    /// <summary>Why the figures are changing. It appears next to the version in the history.</summary>
    public string? RevisionReason { get; init; }
}

/// <summary>Edits a Draft version in place. Refused on anything that has been submitted.</summary>
public sealed record UpdateBudgetPlanVersionRequest
{
    public long ExpectedVersion { get; init; }

    public decimal TargetAmount { get; init; }

    public decimal BudgetAmount { get; init; }

    public Guid? CurrencyId { get; init; }

    public string BudgetCategory { get; init; } = string.Empty;

    public int ExpectedVolume { get; init; }

    public string? Assumptions { get; init; }

    /// <summary>The owner may be reassigned while a plan is still a draft.</summary>
    public Guid? OwnerUserId { get; init; }
}

/// <summary>Sends a draft version for approval.</summary>
public sealed record SubmitBudgetPlanVersionRequest
{
    public long ExpectedVersion { get; init; }

    public string? Note { get; init; }
}

/// <summary>Approves or rejects a submitted version.</summary>
public sealed record BudgetPlanDecisionRequest
{
    public long ExpectedVersion { get; init; }

    /// <summary>
    /// Required on a rejection.
    ///
    /// A rejection without a reason tells the person who submitted it nothing about what to change,
    /// so they resubmit the same figures and the loop repeats.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>Filters the plan register.</summary>
public sealed record BudgetPlanSearchFilter
{
    public string? Search { get; init; }

    public Guid? CampaignId { get; init; }

    public Guid? OwnerUserId { get; init; }

    public string? PlanPeriod { get; init; }

    public string? TargetDimension { get; init; }

    public PlanApprovalState? ApprovalState { get; init; }

    /// <summary>Only plans that have a version currently in force.</summary>
    public bool? HasApprovedVersion { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public string? Sort { get; init; }
}

// =================================================================================================
// Responses
// =================================================================================================

/// <summary>One version of a plan, exactly as it stands.</summary>
public sealed record BudgetPlanVersionResponse
{
    public Guid Id { get; init; }

    public int VersionNumber { get; init; }

    /// <summary>The label the screen shows, e.g. "v3".</summary>
    public string VersionLabel { get; init; } = string.Empty;

    public decimal TargetAmount { get; init; }

    public decimal BudgetAmount { get; init; }

    public Guid CurrencyId { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string BudgetCategory { get; init; } = string.Empty;

    public int ExpectedVolume { get; init; }

    public string? Assumptions { get; init; }

    public PlanApprovalState ApprovalState { get; init; }

    public string ApprovalStateDescription { get; init; } = string.Empty;

    public Guid? SubmittedByUserId { get; init; }

    public DateTimeOffset? SubmittedAtUtc { get; init; }

    public Guid? ApprovedByUserId { get; init; }

    public DateTimeOffset? ApprovedAtUtc { get; init; }

    public string? DecisionReason { get; init; }

    public DateTimeOffset? EffectiveAtUtc { get; init; }

    /// <summary>
    /// What has actually come in against this plan.
    ///
    /// ONLY MEANINGFUL ON THE APPROVED VERSION, because that is the one the campaign is being run
    /// to. It is computed from the donations attributed to the campaign rather than stored, so it
    /// cannot drift from the donations it is supposed to summarise.
    /// </summary>
    public decimal ActualReconciledAmount { get; init; }

    /// <summary>Actual minus target. Negative means short of the target.</summary>
    public decimal Variance { get; init; }

    /// <summary>The variance as a percentage of the target. Zero when the target is zero.</summary>
    public decimal VariancePercentage { get; init; }

    public long Version { get; init; }

    public bool IsEditable { get; init; }

    public bool CountsTowardTotals { get; init; }
}

/// <summary>One row of the plan register.</summary>
public sealed record BudgetPlanListItemResponse
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid CampaignId { get; init; }

    public string CampaignCode { get; init; } = string.Empty;

    public string CampaignName { get; init; } = string.Empty;

    public string PlanPeriod { get; init; } = string.Empty;

    public string TargetDimension { get; init; } = string.Empty;

    public Guid OwnerUserId { get; init; }

    /// <summary>The version the row shows: the approved one when there is one, else the latest.</summary>
    public BudgetPlanVersionResponse? DisplayVersion { get; init; }

    /// <summary>True when a distinct approved version exists and counts toward the campaign totals.</summary>
    public bool HasApprovedVersion { get; init; }

    public int VersionCount { get; init; }

    public long Version { get; init; }

    public IReadOnlyList<string> PermittedActions { get; init; } = [];
}

/// <summary>A plan with its full version history.</summary>
public sealed record BudgetPlanDetailResponse
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid TenantId { get; init; }

    public Guid BusinessUnitId { get; init; }

    public Guid CampaignId { get; init; }

    public string CampaignCode { get; init; } = string.Empty;

    public string CampaignName { get; init; } = string.Empty;

    public string PlanPeriod { get; init; } = string.Empty;

    public string TargetDimension { get; init; } = string.Empty;

    public Guid OwnerUserId { get; init; }

    public IReadOnlyList<BudgetPlanVersionResponse> Versions { get; init; } = [];

    public BudgetPlanVersionResponse? ApprovedVersion { get; init; }

    public BudgetPlanVersionResponse? LatestVersion { get; init; }

    public bool HasApprovedVersion { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public Guid? UpdatedByUserId { get; init; }

    public long Version { get; init; }

    public IReadOnlyList<string> PermittedActions { get; init; } = [];
}

/// <summary>
/// A campaign's committed budget and target, summed across its plans.
///
/// APPROVED VERSIONS ONLY. Summing anything else would count figures nobody has agreed to, and
/// counting more than one version per plan is the double counting the whole design exists to
/// prevent.
/// </summary>
public sealed record CampaignBudgetSummaryResponse
{
    public Guid CampaignId { get; init; }

    public string CampaignCode { get; init; } = string.Empty;

    public decimal CommittedTargetAmount { get; init; }

    public decimal CommittedBudgetAmount { get; init; }

    public int CommittedExpectedVolume { get; init; }

    public decimal ActualReconciledAmount { get; init; }

    public decimal Variance { get; init; }

    public decimal VariancePercentage { get; init; }

    public int PlanCount { get; init; }

    public int ApprovedPlanCount { get; init; }

    /// <summary>Plans awaiting a decision - what stands between the campaign and a settled budget.</summary>
    public int AwaitingApprovalCount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;
}
