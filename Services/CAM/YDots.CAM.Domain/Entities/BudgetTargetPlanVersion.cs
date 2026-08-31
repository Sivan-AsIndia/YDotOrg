using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One immutable version of a budget and target plan.
///
/// REVISING A PLAN APPENDS; IT NEVER OVERWRITES. A version that has been submitted or approved is
/// the record of what somebody actually agreed to, and editing it in place would rewrite that
/// agreement after the fact. Only a Draft version can be edited, and only because a draft has not
/// been agreed to by anybody yet.
///
/// SEGREGATION OF DUTIES IS RECORDED, NOT ASSUMED. <see cref="SubmittedByUserId"/> and
/// <see cref="ApprovedByUserId"/> are both stored so the rule that they must differ can be checked
/// against the record rather than against whatever the requesting client claims.
/// </summary>
public sealed class BudgetTargetPlanVersion : AuditEntity
{
    public Guid BudgetTargetPlanId { get; set; }

    /// <summary>1-based, minted by the plan and never reused - it is how a version is cited.</summary>
    public int VersionNumber { get; set; }

    /// <summary>What this plan intends to raise.</summary>
    public decimal TargetAmount { get; set; }

    /// <summary>What it intends to spend to raise it.</summary>
    public decimal BudgetAmount { get; set; }

    /// <summary>
    /// The currency both amounts are in.
    ///
    /// STORED PER VERSION rather than on the plan. A plan revised after a campaign's currency was
    /// corrected must keep the old version's figures readable in the currency they were agreed in;
    /// reinterpreting historic amounts in a new currency would silently restate them.
    /// </summary>
    public Guid CurrencyId { get; set; }

    public string BudgetCategory { get; set; } = string.Empty;

    /// <summary>The number of gifts, sign-ups or responses the plan expects.</summary>
    public int ExpectedVolume { get; set; }

    /// <summary>What the figures rest on, in the planner's own words.</summary>
    public string? Assumptions { get; set; }

    public PlanApprovalState ApprovalState { get; set; } = PlanApprovalState.Draft;

    public Guid? SubmittedByUserId { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    /// <summary>Why a version was rejected, so a reviser knows what to change.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>When this version reached the state it is in - what the screen labels "effective".</summary>
    public DateTimeOffset? EffectiveAtUtc { get; set; }

    public BudgetTargetPlan Plan { get; set; } = default!;

    /// <summary>
    /// True while the version can still be edited in place.
    ///
    /// A DRAFT ONLY. Everything else is a record of a decision somebody took, and the way to change
    /// those figures is to revise the plan into a new version - which leaves what was decided
    /// visible next to what replaced it.
    /// </summary>
    public bool IsEditable => ApprovalState == PlanApprovalState.Draft;

    /// <summary>True when this version is the one whose figures count toward the campaign's totals.</summary>
    public bool CountsTowardTotals => ApprovalState == PlanApprovalState.Approved;
}
