using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A budget and target plan for a campaign, under a stable reference that never changes.
///
/// THE PLAN IS THE IDENTITY; THE VERSIONS ARE THE FIGURES. A plan named "Q3 Education · Retail" is
/// referred to by one code for its whole life, while the amounts underneath it are revised, and
/// each revision is a new immutable version. That split is what lets somebody ask "what were we
/// working to in August?" and get an answer, which a plan that overwrote its own numbers could
/// never give.
///
/// AT MOST ONE VERSION MAY BE APPROVED AT A TIME. This is the rule the whole design turns on: a
/// campaign's committed budget is the sum of the approved version of each plan, and two approved
/// versions of one plan would double-count it. It is enforced by a filtered unique index in the
/// database as well as by the handler, because the handler's check is a read followed by a write
/// and two simultaneous approvals can both pass it.
/// </summary>
public sealed class BudgetTargetPlan : TenantEntity, ICodedEntity
{
    /// <summary>
    /// The stable reference, minted once by the server.
    ///
    /// SERVER-MINTED, NEVER CLIENT-SUPPLIED. The screen used to compose one in the browser, which
    /// meant two people allocating a plan at the same moment could mint the same reference - and a
    /// plan reference is what a finance team quotes in correspondence.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public Guid CampaignId { get; set; }

    /// <summary>
    /// The period the plan covers, e.g. "FY2026-27 Q3".
    ///
    /// FREE TEXT, DELIBERATELY. Organisations run to different financial calendars, and forcing a
    /// quarter enum would have meant either refusing plans that do not fit it or silently
    /// misfiling them.
    /// </summary>
    public string PlanPeriod { get; set; } = string.Empty;

    /// <summary>
    /// What the target is measured along - channel, region, donor segment.
    ///
    /// IT IS PART OF THE PLAN'S IDENTITY, not of its figures: the same campaign and period can
    /// carry one plan per dimension, and the duplicate rule below is what stops two plans covering
    /// the same ground.
    /// </summary>
    public string TargetDimension { get; set; } = string.Empty;

    /// <summary>The person accountable for the plan. An IAM user id, not a name.</summary>
    public Guid OwnerUserId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public ICollection<BudgetTargetPlanVersion> Versions { get; set; } = [];

    /// <summary>The version currently in force, if any. Null while a plan has never been approved.</summary>
    public BudgetTargetPlanVersion? ApprovedVersion =>
        Versions.FirstOrDefault(version => version.ApprovalState == PlanApprovalState.Approved);

    /// <summary>The most recent version, whatever its state - what the screen shows by default.</summary>
    public BudgetTargetPlanVersion? LatestVersion =>
        Versions.OrderByDescending(version => version.VersionNumber).FirstOrDefault();

    /// <summary>The next version number to mint. Versions are numbered from 1 and never reused.</summary>
    public int NextVersionNumber =>
        Versions.Count == 0 ? 1 : Versions.Max(version => version.VersionNumber) + 1;
}
