using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>Query string of the follow-up planner (DON-UI-08).</summary>
public sealed class FollowUpSearchFilter : PaginationRequest
{
    /// <summary>Free text over follow-up reference, donor reference and lead reference.</summary>
    public string? Search { get; set; }

    public Guid? DonorId { get; set; }

    public Guid? LeadId { get; set; }

    public Guid? RelationshipOwnerUserId { get; set; }

    public FollowUpStatus? Status { get; set; }

    public FollowUpPriority? Priority { get; set; }

    public ConsentChannel? PermittedChannel { get; set; }

    public string? PreferredLanguage { get; set; }

    public DateTimeOffset? DueAfterUtc { get; set; }

    public DateTimeOffset? DueBeforeUtc { get; set; }

    /// <summary>True returns only the caller's own tasks.</summary>
    public bool? OnlyMine { get; set; }
}
