using YDots.CAM.Application.Common.Models;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Campaigns.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Creating a campaign.
///
/// NO OrganisationId FIELD, and that absence is the security control. The Organisation comes
/// from the token and is stamped by the DbContext; a caller who could name it could create a
/// campaign inside somebody else's Organisation.
///
/// <c>Status</c> accepts only Draft or Submitted. Everything past that is reached through the
/// lifecycle endpoints, each of which has its own permission and its own rules - letting a
/// create call arrive with Status = Active would route around all of them.
/// </summary>
// TargetAmount and BudgetAmount ARE DELIBERATELY ABSENT while Target & Budget is on hold. No
// screen collects either, so carrying them on this contract only offered a money field nothing
// validated and nothing displayed. A campaign is created with a target of 0 until the module
// returns and brings its own request with it.
public sealed record CreateCampaignRequest(
    string Name,
    string Code,
    string Purpose,
    string FundOrProgramme,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid CurrencyId,
    Guid CountryId,
    IReadOnlyList<Guid> OwnerIds,
    Guid? StateId = null,
    Guid? CityId = null,
    string? ZipCode = null,
    LifecycleActivation LifecycleActivation = LifecycleActivation.Manual,
    int DaysBeforeStart = 0,
    TimeOnly ReminderTime = default,
    string? PublicDescription = null,
    string? TermsAndNotice = null,
    CampaignStatus Status = CampaignStatus.Draft,
    IReadOnlyList<Guid>? ChannelIds = null);

/// <summary>
/// Editing a campaign.
///
/// <c>Status</c> IS ABSENT, unlike on the create request, and unlike the version this replaces.
/// The old update command carried a Status field, which meant an edit could move a campaign
/// from Draft straight to Closed without touching a lifecycle endpoint - bypassing the
/// approval rules, the segregation-of-duties check and the lifecycle audit rows in one PUT.
/// Status changes now go through the lifecycle endpoints and nowhere else.
/// </summary>
// Absent here for the same reason as on create, and with one extra consequence worth stating: an
// edit used to assign campaign.TargetAmount = request.TargetAmount unconditionally. Since the
// wizard omits the field, that assignment wrote 0 over whatever target the record already held, so
// editing a campaign silently erased its target. Leaving the field off the contract means an edit
// cannot touch it at all.
public sealed record UpdateCampaignRequest(
    long ExpectedVersion,
    string Name,
    string Purpose,
    string FundOrProgramme,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid CurrencyId,
    Guid CountryId,
    IReadOnlyList<Guid> OwnerIds,
    Guid? StateId = null,
    Guid? CityId = null,
    string? ZipCode = null,
    LifecycleActivation LifecycleActivation = LifecycleActivation.Manual,
    int DaysBeforeStart = 0,
    TimeOnly ReminderTime = default,
    string? PublicDescription = null,
    string? TermsAndNotice = null,
    IReadOnlyList<Guid>? ChannelIds = null);

/// <summary>
/// The body of a lifecycle transition: submit, approve, activate, pause, resume, request
/// close, approve close, delete draft.
///
/// ONE REQUEST TYPE FOR ALL EIGHT, because the ROUTE says which transition is meant and each
/// route carries its own permission. The eight near-identical request classes this replaces
/// differed only in which optional reason fields they exposed, and the endpoint could not have
/// told them apart anyway.
///
/// Which fields are REQUIRED varies by transition and is enforced by the handler: a close
/// request needs a reason category and a detailed reason, while resuming needs neither.
/// </summary>
public sealed record CampaignLifecycleRequest(
    long ExpectedVersion,
    string? ReasonCategory = null,
    string? DetailedReason = null,
    string? CommunicationImpact = null,
    string? ClosureSummary = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the campaign register. Deliberately narrower than the detail response.</summary>
public sealed record CampaignListItemResponse(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string FundOrProgramme,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TargetAmount,
    decimal? BudgetAmount,
    Guid CurrencyId,
    CampaignStatus Status,
    string StatusDescription,

    /// <summary>How far through its own dates the campaign is, as a percentage. Null before it starts.</summary>
    int? ElapsedPercent,

    int OwnerCount,

    /// <summary>
    /// The campaign's accountable owners, by IAM user id.
    ///
    /// ON THE LIST PROJECTION, not only on the detail, because the register draws an owner column
    /// and had nothing to draw it from. With only a COUNT here, every row on the campaign register
    /// and every owner card on the campaign detail read "Unassigned" after a page load, whatever
    /// had been chosen in the wizard - the names only appeared for as long as the browser still
    /// held the record it had just created in memory.
    /// </summary>
    IReadOnlyList<Guid> OwnerIds,

    int TrackingAssetCount,

    /// <summary>Required readiness checks not yet passed. Non-zero means it cannot launch.</summary>
    int OutstandingCheckCount,

    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full campaign record behind the detail screen.</summary>
public sealed record CampaignDetailResponse(
    Guid Id,
    Guid TenantId,
    Guid BusinessUnitId,
    string Code,
    string Name,
    string Purpose,
    string FundOrProgramme,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TargetAmount,
    Guid CurrencyId,
    decimal? BudgetAmount,
    Guid CountryId,
    Guid? StateId,
    Guid? CityId,
    string? ZipCode,
    LifecycleActivation LifecycleActivation,
    int DaysBeforeStart,
    TimeOnly ReminderTime,
    string? PublicDescription,
    string? TermsAndNotice,
    CampaignStatus Status,
    string StatusDescription,
    IReadOnlyList<Guid> OwnerIds,
    IReadOnlyList<Guid> ChannelIds,
    Guid? SubmittedByUserId,
    DateTimeOffset? SubmittedAtUtc,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,

    /// <summary>The outstanding close request, when one is pending approval.</summary>
    CampaignLifecycleActionResponse? PendingCloseRequest,

    /// <summary>
    /// What THIS caller may do to the campaign next, decided from the record's state AND the
    /// caller's permissions together - including the segregation-of-duties rule, so Approve is
    /// absent for the person who submitted it. Render buttons from this list and they can never
    /// disagree with what the API will allow.
    /// </summary>
    IReadOnlyList<string> PermittedActions);

/// <summary>One lifecycle transition, as the detail screen and the history tab show it.</summary>
public sealed record CampaignLifecycleActionResponse(
    Guid Id,
    CampaignLifecycleActionType ActionType,
    string ActionTypeDescription,
    CampaignLifecycleActionStatus ActionStatus,
    DateTimeOffset EffectiveAtUtc,
    string? ReasonCategory,
    string? DetailedReason,
    string? CommunicationImpact,
    string? ClosureSummary,
    Guid? RequestedByUserId,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>One row of the campaign audit trail.</summary>
public sealed record CampaignHistoryResponse(
    Guid Id,
    string ActionCode,
    Guid? ActorUserId,
    string TargetType,
    Guid TargetId,
    AuditResult Result,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>Counts by status, for the register's summary tiles.</summary>
public sealed record CampaignStatisticsResponse(
    int Total,
    int Draft,
    int Submitted,
    int Approved,
    int Scheduled,
    int Active,
    int Paused,
    int Closing,
    int Closed,
    int Cancelled);

/// <summary>One line of the campaign export. Flat and all-string: a CSV column has no enums.</summary>
public sealed record CampaignExportRow(
    string Code,
    string Name,
    string FundOrProgramme,
    string StartDate,
    string EndDate,
    string TargetAmount,
    string? BudgetAmount,
    string Status,
    string OwnerCount,
    string TrackingAssetCount,
    string? UpdatedAtUtc);

// =====================================================================================
// Filters
// =====================================================================================

/// <summary>What the campaign register can be narrowed by.</summary>
public sealed class CampaignSearchFilter : PaginationRequest
{
    public CampaignStatus? Status { get; set; }

    public Guid? CurrencyId { get; set; }

    public Guid? CountryId { get; set; }

    /// <summary>Campaigns this user owns. Answers "my campaigns" without a second endpoint.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Campaigns whose dates overlap this window.</summary>
    public DateOnly? StartsOnOrAfter { get; set; }

    public DateOnly? EndsOnOrBefore { get; set; }

    /// <summary>
    /// Campaigns running right now: Active, and inside their own dates.
    ///
    /// A separate flag rather than a Status filter, because "Active" the status and "running
    /// today" are different questions - an Active campaign whose end date has passed is exactly
    /// the row an operator is looking for when they ask which ones need closing.
    /// </summary>
    public bool? IsRunningNow { get; set; }
}
