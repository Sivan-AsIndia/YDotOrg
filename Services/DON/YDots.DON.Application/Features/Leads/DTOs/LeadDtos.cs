using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.Leads.DTOs;

/// <summary>
/// SCR-DON-002 Save body. One field per row of the lead capture field contract.
///
/// The consent block is embedded here rather than living on a separate screen. That is the
/// approved pattern: "Collect consent" is a toggle on this form, and when it is on the channel
/// decisions come with it, so a lead and its permission-to-contact evidence are captured in one
/// act instead of two.
/// </summary>
public sealed class CreateLeadRequest
{
    /// <summary>"First name or known name". Required.</summary>
    public string FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; }

    /// <summary>E.164. Conditional: a lead needs at least one way of being reached.</summary>
    public string? MobileNumber { get; set; }

    public string? EmailAddress { get; set; }

    public string? PreferredLanguage { get; set; }

    public string? City { get; set; }

    public string? GeographyCode { get; set; }

    /// <summary>Required. Must be an active campaign inside the caller's scope.</summary>
    public Guid CampaignId { get; set; }

    /// <summary>Required. Where the lead came from.</summary>
    public string Source { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTimeOffset? PreferredContactTimeUtc { get; set; }

    /// <summary>Optional first owner. Defaults to the caller when left empty.</summary>
    public Guid? OwnerUserId { get; set; }

    public string? OwnerName { get; set; }

    public string? TeamCode { get; set; }

    public string? NextAction { get; set; }

    public DateTimeOffset? NextActionDueUtc { get; set; }

    /// <summary>The embedded consent block. Null or CollectConsent = false means no consent captured.</summary>
    public LeadConsentRequest? Consent { get; set; }
}

/// <summary>SCR-DON-002 edit body. Same fields plus the concurrency version.</summary>
public sealed class UpdateLeadRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; }

    public string? MobileNumber { get; set; }

    public string? EmailAddress { get; set; }

    public string? PreferredLanguage { get; set; }

    public string? City { get; set; }

    public string? GeographyCode { get; set; }

    public Guid CampaignId { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTimeOffset? PreferredContactTimeUtc { get; set; }

    public string? NextAction { get; set; }

    public DateTimeOffset? NextActionDueUtc { get; set; }

    public LeadConsentRequest? Consent { get; set; }

    public long ExpectedVersion { get; set; }
}

/// <summary>
/// The consent section of lead capture. The toggle is explicit: CollectConsent has to be true
/// before anything below it is read, and legal acknowledgements are never pre-selected.
/// </summary>
public sealed class LeadConsentRequest
{
    /// <summary>The "Collect consent" toggle. False hides the section and stores nothing.</summary>
    public bool CollectConsent { get; set; }

    public bool EmailConsent { get; set; }

    public bool SmsConsent { get; set; }

    public bool WhatsAppConsent { get; set; }

    public bool PhoneCallConsent { get; set; }

    /// <summary>Where the consent was collected: web form, call recording, paper form.</summary>
    public string? ConsentSource { get; set; }

    public DateTimeOffset? ConsentDateUtc { get; set; }

    public string? ConsentNotes { get; set; }

    /// <summary>Stable reference of the uploaded evidence document.</summary>
    public string? ConsentEvidenceReference { get; set; }

    /// <summary>Why the permission is being asked for. 10 to 2000 characters when consent is collected.</summary>
    public string? Purpose { get; set; }
}

/// <summary>
/// One row of the lead work queue grid. Contact values arrive masked by default.
///
/// NAME, MOBILE AND EMAIL ARE SEPARATE FIELDS as well as combined into
/// <see cref="NameAndContactPreview"/>, because the grid in the module brief gives each of them
/// its own sortable column. The combined preview is kept for the callers that show one line, and
/// both obey the same masking rule: without
/// <c>don.donors.view-sensitive-contact</c> the contact halves come back masked, never raw.
/// </summary>
public sealed record LeadListItemResponse(
    Guid Id,
    string LeadReference,
    string NameAndContactPreview,
    string Name,
    string? MobileNumber,
    string? EmailAddress,
    string? CampaignName,
    Guid? OwnerUserId,
    string? OwnerName,
    string Status,
    string? Source,

    /// <summary>How engaged the lead is: Cold, Warm or Hot. A grid column and a filter.</summary>
    string Temperature,

    /// <summary>How much they might give: Low, Medium or High. A grid column and a filter.</summary>
    string DonationPotential,

    /// <summary>Lead health 0-100, recomputed on read so the recency component is never stale.</summary>
    int HealthScore,

    string? NextAction,
    DateTimeOffset? NextActionDueUtc,
    string SlaState,
    string LastContactOutcome,
    string PreferredLanguage,

    /// <summary>True once a donation has converted this lead. The queue hides these by default.</summary>
    bool IsConverted,

    /// <summary>The donor the lead became, so the row can link straight to Donor 360.</summary>
    Guid? ConvertedDonorId,

    DateTimeOffset UpdatedAtUtc,
    long Version,
    bool IsContactMasked,
    IReadOnlyList<string> PermittedActions);

/// <summary>The full lead record behind SCR-DON-002 and the lead panel of the work queue.</summary>
public sealed record LeadDetailResponse(
    Guid Id,
    string LeadReference,
    string FirstName,
    string? LastName,
    string? MobileNumber,
    string? EmailAddress,
    string PreferredLanguage,
    string? City,
    string? GeographyCode,
    Guid CampaignId,
    string? CampaignName,
    string Source,
    string ConsentState,
    string? ConsentEvidenceReference,
    string? Notes,
    DateTimeOffset? PreferredContactTimeUtc,
    string? DuplicateCandidateSummary,
    string Status,
    Guid? OwnerUserId,
    string? OwnerName,
    string? TeamCode,
    string? NextAction,
    DateTimeOffset? NextActionDueUtc,
    string SlaState,
    string LastContactOutcome,
    DateTimeOffset? LastContactedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? QualifiedAtUtc,
    Guid? ConvertedDonorId,
    DateTimeOffset? ConvertedAtUtc,
    string? ClosureReason,
    bool IsDraft,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    bool IsContactMasked,
    bool IsEvidenceMasked,
    IReadOnlyList<LeadConsentSummaryResponse> Consents,
    IReadOnlyList<string> PermittedActions);

/// <summary>One consent decision shown beside a lead or a donor.</summary>
public sealed record LeadConsentSummaryResponse(
    Guid Id,
    string Channel,
    string ConsentState,
    string Status,
    string NoticeVersion,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset? ExpiryAtUtc);

/// <summary>
/// SCR-DON-002 Deduplicate result. Deliberately vague about the other person: a category and a
/// route, never their name, e-mail or phone. UI section 4.2.4: "Show a safe candidate category
/// and comparison route without exposing another person's protected details."
/// </summary>
public sealed record DuplicateCandidateResponse(
    Guid CandidateId,
    string CandidateType,
    string MatchCategory,
    string Confidence,
    string SafeSummary,
    string ComparisonRoute);

/// <summary>What the Deduplicate action returns.</summary>
public sealed record DeduplicateResultResponse(
    Guid LeadId,
    string LeadReference,
    int CandidateCount,
    string State,
    string Message,
    IReadOnlyList<DuplicateCandidateResponse> Candidates);

/// <summary>Autocomplete row for the lead selectors on the follow-up planner.</summary>
public sealed record LeadLookupResponse(Guid Id, string LeadReference, string DisplayName, string Status);
