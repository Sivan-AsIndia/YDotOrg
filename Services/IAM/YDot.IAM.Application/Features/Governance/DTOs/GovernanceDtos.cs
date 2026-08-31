using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Governance.DTOs;

// =====================================================================================
// Access requests
// =====================================================================================

/// <summary>
/// Asking for access somebody does not currently have.
///
/// <c>BusinessJustification</c> is not decoration. It is the thing an auditor reads a year
/// later to understand why a permission was granted, so it is 10 to 1000 characters and
/// mandatory.
/// </summary>
public sealed record CreateAccessRequestRequest(
    Guid RequestedForUserId,
    AccessRequestType RequestType,
    string BusinessJustification,
    Guid? RoleId = null,
    string? PermissionCode = null,
    DataScopeType? ScopeType = null,
    string? ScopeValue = null,
    DateTimeOffset? AccessStartsAtUtc = null,

    /// <summary>Required for temporary access. Null means a permanent grant.</summary>
    DateTimeOffset? AccessEndsAtUtc = null,

    /// <summary>Submits straight away rather than leaving it as a draft.</summary>
    bool SubmitImmediately = true);

/// <summary>Editing a draft request.</summary>
public sealed record UpdateAccessRequestRequest(
    long ExpectedVersion,
    string? BusinessJustification = null,
    Guid? RoleId = null,
    DateTimeOffset? AccessStartsAtUtc = null,
    DateTimeOffset? AccessEndsAtUtc = null);

/// <summary>Submitting a draft for a decision.</summary>
public sealed record SubmitAccessRequestRequest(long ExpectedVersion, string? Comment = null);

/// <summary>
/// Deciding a request.
///
/// The approver may not be the requester — checked in the handler, not only on the screen.
/// Approval writes the actual assignment and stamps it with this request id, so the grant can
/// always be traced back to the justification that earned it.
/// </summary>
public sealed record DecideAccessRequestRequest(
    bool Approved,
    long ExpectedVersion,
    string? Notes = null,

    /// <summary>Lets the approver grant less than was asked for.</summary>
    DateTimeOffset? AccessEndsAtUtc = null);

/// <summary>
/// Sending a request back for more information.
///
/// NOT A REJECTION, and the difference matters to the person on the other end. A rejection is a
/// decision — the answer is no. A return says the approver cannot answer yet, almost always
/// because the justification does not explain what the access is actually for. The request keeps
/// its number and its history and goes back to the requester to be improved.
/// </summary>
public sealed record ReturnAccessRequestRequest(string Reason, long ExpectedVersion);

/// <summary>Handing a review to somebody better placed to answer it.</summary>
public sealed record DelegateAccessReviewRequest(
    Guid ReviewerUserId,
    string Reason,
    long ExpectedVersion);

/// <summary>
/// Escalating a review the reviewer cannot answer.
///
/// Mechanically the same handover as a delegation; different in meaning, and recorded as such. A
/// delegation says "you are better placed to answer this". An escalation says "this access looks
/// wrong and removing it is above my authority", which is exactly the signal a governance report
/// should be able to count.
/// </summary>
public sealed record EscalateAccessReviewRequest(
    Guid EscalateToUserId,
    string Reason,
    long ExpectedVersion);

/// <summary>Withdrawing a request before it is decided.</summary>
public sealed record WithdrawAccessRequestRequest(string Reason, long ExpectedVersion);

// =====================================================================================
// Access reviews
// =====================================================================================

/// <summary>Raising a batch of reviews.</summary>
public sealed record CreateAccessReviewCampaignRequest(
    string Name,
    DateTimeOffset DueAtUtc,
    string? Code = null,
    string? Description = null,
    DateTimeOffset? StartsAtUtc = null,

    /// <summary>
    /// Treats anything still open at the due date as Revoke rather than Retain. Failing
    /// closed is the right default for a recertification: silence should not renew access.
    /// </summary>
    bool RevokeOnNoResponse = false,

    /// <summary>Limits the campaign to these roles. Empty reviews every assignment.</summary>
    IReadOnlyList<Guid>? RoleIds = null,

    /// <summary>Limits the campaign to these users. Empty reviews everybody.</summary>
    IReadOnlyList<Guid>? UserIds = null,

    /// <summary>Only the assignments that carry a sensitive permission.</summary>
    bool SensitiveOnly = false);

/// <summary>Raising a single review outside any campaign.</summary>
public sealed record CreateAccessReviewRequest(
    Guid SubjectUserId,
    Guid ReviewerUserId,
    DateTimeOffset ReviewDueAtUtc,
    Guid? UserRoleId = null,
    Guid? RoleId = null);

/// <summary>
/// Recording a decision. A reason is required for Modify and Revoke, because those are the
/// two that take something away and the person losing it deserves an explanation.
/// </summary>
public sealed record DecideAccessReviewRequest(
    AccessReviewDecision Decision,
    long ExpectedVersion,
    string? DecisionReason = null,

    /// <summary>
    /// Carries the decision out immediately. When false it is recorded and applied when the
    /// campaign closes, which is how a batch recertification usually runs.
    /// </summary>
    bool ApplyImmediately = true);

/// <summary>Cancelling a review or a campaign.</summary>
public sealed record CancelAccessReviewRequest(string Reason, long ExpectedVersion);

/// <summary>Closing a campaign and applying every outstanding decision.</summary>
public sealed record CloseAccessReviewCampaignRequest(long ExpectedVersion, string? Notes = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the access request queue.</summary>
public sealed record AccessRequestListItemResponse(
    Guid Id,
    string RequestNumber,
    Guid RequestedForUserId,
    string RequestedForName,
    string RequestedByName,
    AccessRequestType RequestType,
    string RequestTypeDisplay,
    string? RoleName,
    string? PermissionCode,
    AccessRequestStatus Status,
    string StatusDisplay,
    bool IsSensitive,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset AccessStartsAtUtc,
    DateTimeOffset? AccessEndsAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedByName,

    /// <summary>True when the current caller may decide this one, so the queue can highlight it.</summary>
    bool CanDecide,

    long Version);

/// <summary>A request with everything a decision needs.</summary>
public sealed record AccessRequestDetailResponse(
    Guid Id,
    string RequestNumber,
    Guid RequestedForUserId,
    string RequestedForName,
    string RequestedForEmail,
    Guid RequestedByUserId,
    string RequestedByName,
    AccessRequestType RequestType,
    string RequestTypeDisplay,
    Guid? RoleId,
    string? RoleName,
    string? PermissionCode,
    DataScopeType? ScopeType,
    string? ScopeValue,
    string BusinessJustification,
    DateTimeOffset AccessStartsAtUtc,
    DateTimeOffset? AccessEndsAtUtc,
    AccessRequestStatus Status,
    string StatusDisplay,
    bool IsSensitive,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DecidedByUserId,
    string? DecidedByName,
    string? DecisionNotes,
    DateTimeOffset? WithdrawnAtUtc,
    string? WithdrawalReason,
    Guid? GrantedUserRoleId,
    DateTimeOffset CreatedAtUtc,
    long Version,

    /// <summary>What the requested access would add, so the approver sees the consequence.</summary>
    IReadOnlyList<string> PermissionsGranted,

    IReadOnlyList<string> SegregationOfDutiesConflicts,
    bool CanDecide,
    IReadOnlyList<string> PermittedActions);

/// <summary>One row of the review queue.</summary>
public sealed record AccessReviewListItemResponse(
    Guid Id,
    string ReviewNumber,
    Guid? CampaignId,
    string? CampaignName,
    Guid SubjectUserId,
    string SubjectName,
    string ReviewerName,
    string? RoleName,
    AccessReviewStatus Status,
    string StatusDisplay,
    AccessReviewDecision? Decision,
    DateTimeOffset ReviewDueAtUtc,
    bool IsOverdue,
    DateTimeOffset? DecidedAtUtc,
    bool IsDecisionApplied,
    bool IsAssignedToMe,
    long Version);

/// <summary>A review with the snapshot of what is being recertified.</summary>
public sealed record AccessReviewDetailResponse(
    Guid Id,
    string ReviewNumber,
    Guid? CampaignId,
    string? CampaignName,
    Guid SubjectUserId,
    string SubjectName,
    string SubjectEmail,
    Guid ReviewerUserId,
    string ReviewerName,
    Guid? UserRoleId,
    Guid? RoleId,
    string? RoleName,
    DateTimeOffset ReviewDueAtUtc,
    AccessReviewDecision? Decision,
    string? DecisionReason,
    DateTimeOffset? DecidedAtUtc,
    AccessReviewStatus Status,
    string StatusDisplay,
    bool IsOverdue,
    bool IsDecisionApplied,
    DateTimeOffset? DecisionAppliedAtUtc,
    int ReminderCount,
    DateTimeOffset? LastRemindedAtUtc,
    long Version,

    /// <summary>
    /// What the person held when the review was raised. A snapshot rather than a live read,
    /// so a later change cannot quietly alter what the reviewer was actually asked about.
    /// </summary>
    IReadOnlyList<string> AccessSnapshot,

    IReadOnlyList<string> PermittedActions);

/// <summary>A campaign with its progress counts.</summary>
public sealed record AccessReviewCampaignResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    AccessReviewCampaignStatus Status,
    string StatusDisplay,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string? ClosedByName,
    int TotalReviewCount,
    int CompletedReviewCount,
    int OverdueReviewCount,
    int PercentComplete,
    bool RevokeOnNoResponse,
    DateTimeOffset CreatedAtUtc,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>IAM-USR-05 request state, shared with the Users slice.</summary>
public sealed record LoginIdentifierChangeResponse(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    bool IsEmailChange,
    string CurrentValue,
    string RequestedValue,
    LoginIdentifierChangeStatus Status,
    string StatusDisplay,
    DateTimeOffset RequestedAtUtc,
    string? RequestedByName,
    string? Reason,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? PreviousOwnerNotifiedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovedByName,
    DateTimeOffset? RejectedAtUtc,
    string? RejectionReason,
    DateTimeOffset? AppliedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool RequiresApproval,
    long Version,
    IReadOnlyList<string> PermittedActions);
