using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One reviewer being asked whether one person should still hold what they hold,
/// section 3.5.
///
/// THE REVIEWER MAY NOT BE THE SUBJECT. Nobody recertifies their own access; that is
/// checked in the handler for sensitive access rather than left to the UI.
///
/// A decision of Modify or Revoke requires a reason, and acting on it writes the change
/// through the same paths an administrator would use, so the resulting audit rows are
/// indistinguishable from a manual change apart from carrying this review id.
/// </summary>
public class AccessReview : TenantEntity
{
    /// <summary>System generated and unique inside the Tenant, for example REV-2026-00042.</summary>
    public string ReviewNumber { get; set; } = string.Empty;

    /// <summary>Null for a one-off review outside any campaign.</summary>
    public Guid? CampaignId { get; set; }

    public AccessReviewCampaign? Campaign { get; set; }

    /// <summary>The person whose access is under review.</summary>
    public Guid SubjectUserId { get; set; }

    public User? SubjectUser { get; set; }

    /// <summary>Cannot equal the subject for sensitive access.</summary>
    public Guid ReviewerUserId { get; set; }

    /// <summary>The specific assignment being recertified. Null reviews the whole account.</summary>
    public Guid? UserRoleId { get; set; }

    public Guid? RoleId { get; set; }

    public Role? Role { get; set; }

    /// <summary>Snapshot of what was held when the review was raised, so a later change
    /// cannot quietly alter what the reviewer was actually asked about.</summary>
    public string? AccessSnapshot { get; set; }

    public DateTimeOffset ReviewDueAtUtc { get; set; }

    public AccessReviewDecision? Decision { get; set; }

    /// <summary>Required on Modify or Revoke. Max 1000.</summary>
    public string? DecisionReason { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public AccessReviewStatus Status { get; set; } = AccessReviewStatus.Open;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>True once the Revoke or Modify has actually been carried out.</summary>
    public bool IsDecisionApplied { get; set; }

    public DateTimeOffset? DecisionAppliedAtUtc { get; set; }

    /// <summary>
    /// Who it was originally assigned to, when it has since been handed on.
    ///
    /// Kept because "who was asked" and "who answered" are different questions, and an audit of
    /// a certification wants both: a review delegated three times before anybody decided is a
    /// different story from one answered by the person it was given to.
    /// </summary>
    public Guid? OriginalReviewerUserId { get; set; }

    /// <summary>Why it was handed on, or escalated. Free text, shown in the trail.</summary>
    public string? DelegationReason { get; set; }

    public DateTimeOffset? DelegatedAtUtc { get; set; }

    public Guid? DelegatedByUserId { get; set; }

    /// <summary>
    /// True when it was escalated rather than delegated.
    ///
    /// Both hand the review to somebody else; the difference is direction and meaning. A
    /// delegation says "you are better placed to answer this". An escalation says "I cannot
    /// answer this and somebody senior must" - which is what a reviewer does when the access
    /// looks wrong and they lack the authority to remove it.
    /// </summary>
    public bool WasEscalated { get; set; }

    public int ReminderCount { get; set; }

    public DateTimeOffset? LastRemindedAtUtc { get; set; }

    public bool IsOverdue(DateTimeOffset asOf) =>
        Status is AccessReviewStatus.Open or AccessReviewStatus.InProgress && ReviewDueAtUtc < asOf;

    public bool IsOpen => Status is AccessReviewStatus.Open or AccessReviewStatus.InProgress;
}
