namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The audit trail for budget and target plans.
///
/// EVERY STATE CHANGE HAS ITS OWN CODE. A plan's figures are what a campaign is run to and reported
/// against, so "who approved this budget, and when" has to be answerable from the audit log alone -
/// not inferred from a row's current state, which only ever shows the latest answer.
/// </summary>
public static class BudgetPlanAuditActionCodes
{
    public const string Allocated = "BUDGET_PLAN_ALLOCATED";
    public const string Revised = "BUDGET_PLAN_REVISED";
    public const string VersionUpdated = "BUDGET_PLAN_VERSION_UPDATED";
    public const string Submitted = "BUDGET_PLAN_SUBMITTED";
    public const string Approved = "BUDGET_PLAN_APPROVED";
    public const string Rejected = "BUDGET_PLAN_REJECTED";
    public const string Superseded = "BUDGET_PLAN_SUPERSEDED";
    public const string Exported = "BUDGET_PLAN_EXPORTED";
}

/// <summary>The audit trail for attribution reads and correction requests.</summary>
public static class AttributionAuditActionCodes
{
    /// <summary>
    /// A CSV of attributed donations left the system.
    ///
    /// Worth recording because the export carries donor-identifying detail alongside amounts - it
    /// is the one attribution action that produces a file outliving the session.
    /// </summary>
    public const string Exported = "ATTRIBUTION_EXPORTED";

    public const string CorrectionRequested = "ATTRIBUTION_CORRECTION_REQUESTED";
}
