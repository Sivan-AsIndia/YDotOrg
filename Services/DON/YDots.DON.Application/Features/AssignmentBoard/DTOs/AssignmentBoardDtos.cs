using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Features.Leads.DTOs;

namespace YDots.DON.Application.Features.AssignmentBoard.DTOs;

/// <summary>
/// GET /api/v1/donors/assignment-board. Balance ownership by team, language, workload and SLA.
/// The board is two lists side by side: the leads waiting to be routed, and the owners who
/// could take them with their current load.
/// </summary>
public sealed record AssignmentBoardResponse(
    string ScreenId,
    string Route,
    PagedResponse<AssignmentBoardRowResponse> Rows,
    IReadOnlyList<OwnerWorkloadResponse> Owners,
    IReadOnlyList<LookupItem> CampaignOptions,
    IReadOnlyList<LookupItem> TeamOptions,
    IReadOnlyList<LookupItem> LanguageOptions,
    IReadOnlyList<LookupItem> WorkloadBandOptions,
    IReadOnlyList<LookupItem> SlaStateOptions,
    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    int BulkRouteMaximumItems,
    string State);

/// <summary>One routable lead, with the owner the board suggests for it.</summary>
public sealed record AssignmentBoardRowResponse(
    Guid LeadId,
    string LeadReference,
    string LeadPreview,
    string? CampaignName,
    Guid? CurrentOwnerUserId,
    string? CurrentOwnerName,
    Guid? SuggestedOwnerUserId,
    string? SuggestedOwnerName,
    string? SuggestionRationale,
    int CurrentOwnerOpenWorkCount,
    string? NextAction,
    DateTimeOffset? NextActionDueUtc,
    string SlaState,
    string PreferredLanguage,
    string? TeamCode,
    string Status,
    long Version);

/// <summary>One candidate owner and how loaded they currently are.</summary>
public sealed record OwnerWorkloadResponse(
    Guid UserId,
    string Name,
    string? TeamCode,
    int OpenWorkCount,
    string WorkloadBand);

/// <summary>POST .../assign and .../reassign. Same body: both are an ownership change.</summary>
public sealed class AssignmentRequest
{
    public Guid LeadId { get; set; }

    public Guid NewOwnerUserId { get; set; }

    public string NewOwnerName { get; set; } = string.Empty;

    public string? TeamCode { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string AssignmentReason { get; set; } = string.Empty;

    public DateTimeOffset? EffectiveAtUtc { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>
/// POST .../bulk-route. UI section 6.2: "Partial processing is explicit per record; no silent
/// skipping", which is why the response reports each lead separately.
/// </summary>
public sealed class BulkRouteRequest
{
    public IList<Guid> LeadIds { get; set; } = [];

    public Guid NewOwnerUserId { get; set; }

    public string NewOwnerName { get; set; } = string.Empty;

    public string? TeamCode { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string AssignmentReason { get; set; } = string.Empty;

    public DateTimeOffset? EffectiveAtUtc { get; set; }
}

/// <summary>The outcome of a bulk route: eligible count, ineligible count and one row each.</summary>
public sealed record BulkRouteResultResponse(
    int RequestedCount,
    int RoutedCount,
    int SkippedCount,
    IReadOnlyList<BulkRouteItemResponse> Items,
    string Message,
    string State);

/// <summary>What happened to one lead in a bulk route.</summary>
public sealed record BulkRouteItemResponse(
    Guid LeadId,
    string? LeadReference,
    bool Routed,
    string Outcome);

/// <summary>GET .../{leadId}/history. The append-only ownership trail behind "Inspect history".</summary>
public sealed record AssignmentHistoryResponse(
    Guid LeadId,
    string LeadReference,
    IReadOnlyList<AssignmentHistoryItemResponse> Items);

/// <summary>One ownership change.</summary>
public sealed record AssignmentHistoryItemResponse(
    Guid Id,
    Guid? PreviousOwnerUserId,
    string? PreviousOwnerName,
    Guid NewOwnerUserId,
    string NewOwnerName,
    string AssignmentReason,
    DateTimeOffset EffectiveAtUtc,
    Guid AssignedByUserId,
    bool IsBulkRoute);

/// <summary>The lead detail panel the board opens beside a row.</summary>
public sealed record AssignmentBoardLeadResponse(LeadDetailResponse Lead, AssignmentHistoryResponse History);
