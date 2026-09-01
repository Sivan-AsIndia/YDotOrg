using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.CampaignReadiness.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>Adding a check to a campaign checklist.</summary>
public sealed record CreateReadinessCheckRequest(
    string CheckName,
    ReadinessCheckCategory Category,
    string SuccessCriteria,
    string? Description = null,
    bool RequiredForLaunch = true,
    Guid? OwnerUserId = null,
    DateOnly? DueDate = null,
    string? Notes = null);

/// <summary>Editing a check. Only a Pending check may be edited.</summary>
public sealed record UpdateReadinessCheckRequest(
    long ExpectedVersion,
    string CheckName,
    ReadinessCheckCategory Category,
    string SuccessCriteria,
    string? Description = null,
    bool RequiredForLaunch = true,
    Guid? OwnerUserId = null,
    DateOnly? DueDate = null,
    string? Notes = null);

/// <summary>
/// Recording a verdict on a check: passed or failed.
///
/// ONE REQUEST FOR BOTH, because the ROUTE says which verdict is meant and each carries its own
/// permission - so an Organisation can let somebody record a failure without letting them sign
/// a check off as passed.
/// </summary>
public sealed record ReadinessVerdictRequest(long ExpectedVersion, string? Notes = null);

/// <summary>Raising a blocker against a check.</summary>
public sealed record AssignReadinessBlockerRequest(
    Guid OwnerUserId,
    string BlockerNote,
    long ExpectedVersion);

/// <summary>Clearing a blocker.</summary>
public sealed record ResolveReadinessBlockerRequest(string? ResolutionNote = null);

/// <summary>Sending a campaign back to Draft from the readiness screen.</summary>
public sealed record ReturnCampaignToDraftRequest(long ExpectedVersion, string Reason);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>
/// The whole checklist for one campaign, with the verdict on whether it may launch.
///
/// <see cref="CanLaunch"/> is computed by the SERVER rather than left to the screen to derive
/// from the counts. The rule - every REQUIRED check passed, and no open blocker on one - is the
/// same rule the activate endpoint enforces, and two copies of it is one that will eventually
/// tell the operator they can launch when the API will refuse.
/// </summary>
public sealed record CampaignReadinessResponse(
    Guid CampaignId,
    string CampaignCode,
    string CampaignName,
    CampaignStatus CampaignStatus,
    int TotalItems,
    int Passed,
    int Failed,
    int Pending,
    int RequiredOutstanding,
    int OpenBlockers,
    decimal ReadinessPercentage,
    bool CanLaunch,
    IReadOnlyList<ReadinessCheckListItemResponse> Items);

/// <summary>One row of the checklist.</summary>
public sealed record ReadinessCheckListItemResponse(
    Guid Id,
    string CheckName,
    string? Description,
    ReadinessCheckCategory Category,
    string CategoryDescription,
    string SuccessCriteria,
    bool RequiredForLaunch,
    Guid? OwnerUserId,
    DateOnly? DueDate,

    /// <summary>True when the due date has passed and the check has not been signed off.</summary>
    bool IsOverdue,

    string? Notes,
    ReadinessCheckStatus Status,
    string StatusDescription,
    bool HasOpenBlocker,

    /// <summary>True when this check is one of the things standing between the campaign and launch.</summary>
    bool BlocksLaunch,

    /// <summary>
    /// The blockers raised against this check, open and resolved.
    ///
    /// ON THE LIST ROW, not only on the detail, because the checklist screen is where a blocker
    /// is cleared and it had no id to clear one WITH. The row carried only <c>HasOpenBlocker</c>,
    /// so the screen synthesised a blocker whose id was the CHECK's id - and
    /// POST /readiness-blockers/{that id}/resolve answers 404 "That blocker was not found",
    /// every time. A blocked check could be created and could never be unblocked.
    ///
    /// They are already loaded: the query behind this Includes them.
    /// </summary>
    IReadOnlyList<ReadinessBlockerResponse> Blockers,

    long Version);

/// <summary>One check in full, with its blockers.</summary>
public sealed record ReadinessCheckDetailResponse(
    Guid Id,
    Guid CampaignId,
    string CheckName,
    string? Description,
    ReadinessCheckCategory Category,
    string CategoryDescription,
    string SuccessCriteria,
    bool RequiredForLaunch,
    Guid? OwnerUserId,
    DateOnly? DueDate,
    bool IsOverdue,
    string? Notes,
    ReadinessCheckStatus Status,
    string StatusDescription,
    bool BlocksLaunch,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<ReadinessBlockerResponse> Blockers,
    IReadOnlyList<string> PermittedActions);

/// <summary>One blocker raised against a check.</summary>
public sealed record ReadinessBlockerResponse(
    Guid Id,
    Guid OwnerUserId,
    string BlockerNote,
    bool IsResolved,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionNote,
    DateTimeOffset CreatedAtUtc);
