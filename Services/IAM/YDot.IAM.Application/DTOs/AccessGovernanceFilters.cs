using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.DTOs;

/// <summary>Filter for the access request queue.</summary>
public sealed class AccessRequestSearchFilter : PaginationRequest
{
    public AccessRequestStatus? Status { get; set; }

    public AccessRequestType? RequestType { get; set; }

    public Guid? RequestedForUserId { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public Guid? RoleId { get; set; }

    public bool? IsSensitive { get; set; }

    /// <summary>Only those the current caller may decide, excluding their own requests.</summary>
    public bool? AwaitingMyDecision { get; set; }

    public DateTimeOffset? SubmittedFromUtc { get; set; }

    public DateTimeOffset? SubmittedToUtc { get; set; }
}

/// <summary>Filter for the access review queue.</summary>
public sealed class AccessReviewSearchFilter : PaginationRequest
{
    public AccessReviewStatus? Status { get; set; }

    public AccessReviewDecision? Decision { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? SubjectUserId { get; set; }

    public Guid? ReviewerUserId { get; set; }

    public bool? IsOverdue { get; set; }

    /// <summary>Only reviews assigned to the current caller.</summary>
    public bool? AssignedToMe { get; set; }

    public DateTimeOffset? DueFromUtc { get; set; }

    public DateTimeOffset? DueToUtc { get; set; }
}
