using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Features.FollowUpPlanner.DTOs;

/// <summary>GET /api/v1/donors/follow-up-planner. Tasks plus every catalogue the form needs.</summary>
public sealed record FollowUpPlannerResponse(
    string ScreenId,
    string Route,
    PagedResponse<FollowUpResponse> FollowUps,
    IReadOnlyList<LookupItem> ChannelOptions,
    IReadOnlyList<LookupItem> PriorityOptions,
    IReadOnlyList<LookupItem> StatusOptions,
    IReadOnlyList<LookupItem> LanguageOptions,
    IReadOnlyList<LookupItem> OwnerOptions,
    string CurrentNoticeVersion,
    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    string State);

/// <summary>One planned follow-up.</summary>
public sealed record FollowUpResponse(
    Guid Id,
    string FollowUpReference,
    Guid? DonorId,
    string? DonorReference,
    string? DonorDisplayName,
    Guid? LeadId,
    string? LeadReference,
    Guid RelationshipOwnerUserId,
    string? RelationshipOwnerName,
    string? Purpose,
    string PermittedChannel,
    string PreferredLanguage,
    DateTimeOffset? PreferredContactTimeUtc,
    string? NextAction,
    DateTimeOffset? DueAtUtc,
    string Priority,
    string? Notes,
    bool ConsentWarningAcknowledged,
    string? ConsentNoticeVersion,
    DateTimeOffset? ConsentAcknowledgedAtUtc,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    string? CompletionOutcome,
    string? RescheduleReason,
    string? CancellationReason,
    DateTimeOffset CreatedAtUtc,
    long Version,
    bool IsNotesMasked,
    bool IsPreferredTimeMasked,
    ConsentWarningResponse ConsentWarning,
    IReadOnlyList<string> PermittedActions);

/// <summary>
/// What the screen has to show before somebody schedules contact. Never pre-ticked: the person
/// scheduling has to read it and accept it, and the acceptance is stored with the notice version.
/// </summary>
public sealed record ConsentWarningResponse(
    bool HasWarning,
    string Level,
    string Message,
    IReadOnlyList<string> PermittedChannels,
    IReadOnlyList<string> ProhibitedChannels);

/// <summary>POST .../schedule-follow-up. The primary action.</summary>
public sealed class ScheduleFollowUpRequest
{
    public Guid? DonorId { get; set; }

    public Guid? LeadId { get; set; }

    public Guid? RelationshipOwnerUserId { get; set; }

    public string? RelationshipOwnerName { get; set; }

    /// <summary>10 to 2000 characters.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Must be a channel consent actually permits.</summary>
    public string PermittedChannel { get; set; } = string.Empty;

    public string? PreferredLanguage { get; set; }

    public DateTimeOffset? PreferredContactTimeUtc { get; set; }

    public string NextAction { get; set; } = string.Empty;

    public DateTimeOffset DueAtUtc { get; set; }

    public string? Priority { get; set; }

    public string? Notes { get; set; }

    /// <summary>Must be true when the consent warning says there is something to acknowledge.</summary>
    public bool ConsentWarningAcknowledged { get; set; }
}

/// <summary>POST .../{id}/assign. Hands the task to a different owner.</summary>
public sealed class AssignFollowUpRequest
{
    public Guid RelationshipOwnerUserId { get; set; }

    public string RelationshipOwnerName { get; set; } = string.Empty;

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string Reason { get; set; } = string.Empty;

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../{id}/mark-complete.</summary>
public sealed class CompleteFollowUpRequest
{
    /// <summary>Required. 10 to 2000 characters.</summary>
    public string CompletionOutcome { get; set; } = string.Empty;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../{id}/reschedule.</summary>
public sealed class RescheduleFollowUpRequest
{
    public DateTimeOffset DueAtUtc { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string RescheduleReason { get; set; } = string.Empty;

    public string? Priority { get; set; }

    public long? ExpectedVersion { get; set; }
}
