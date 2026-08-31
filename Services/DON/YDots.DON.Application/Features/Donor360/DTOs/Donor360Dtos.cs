using YDots.DON.Application.Features.Donors.DTOs;

namespace YDots.DON.Application.Features.Donor360.DTOs;

/// <summary>
/// SCR-DON-003 GET. One payload per panel in the field contract, so the screen makes a single
/// call and every tab already has its content.
/// </summary>
public sealed record Donor360Response(
    string ScreenId,
    string Route,
    string DonorReference,
    DonorDetailResponse Donor,
    IdentityAndContactSummaryResponse IdentityAndContactSummary,
    RelationshipOwnerResponse? RelationshipOwner,
    ConsentStatusResponse ConsentStatus,
    IReadOnlyList<CommunicationPreferenceResponse> CommunicationPreferences,
    IReadOnlyList<DonationTotalResponse> DonationTotalsByStage,
    IReadOnlyList<CampaignHistoryResponse> CampaignHistory,
    IReadOnlyList<ConversationResponse> Conversations,
    IReadOnlyList<Donor360FollowUpResponse> FollowUps,
    IReadOnlyList<PromiseResponse> Promises,
    IReadOnlyList<DocumentResponse> Documents,
    IReadOnlyList<DuplicateLinkResponse> DuplicateLinks,
    IReadOnlyList<ActivityHistoryResponse> ActivityHistory,
    IReadOnlyList<string> PermittedActions,
    IReadOnlyList<string> MaskedFields,
    string ActiveScope,
    string State);

/// <summary>"Identity and contact summary". Restricted: masked unless separately permitted.</summary>
public sealed record IdentityAndContactSummaryResponse(
    string DisplayName,
    string DonorType,
    string? PrimaryEmail,
    string? PrimaryPhone,
    string PreferredLanguage,
    bool DoNotContact,
    IReadOnlyList<DonorContactResponse> AdditionalContacts,
    IReadOnlyList<DonorTagResponse> Tags,
    bool IsMasked);

/// <summary>One additional contact row.</summary>
public sealed record DonorContactResponse(
    Guid Id,
    string Name,
    string? Description,
    string Channel,
    string Value,
    bool IsPrimary,
    bool IsVerified,
    string Status,
    bool IsMasked);

/// <summary>One tag attached to the donor.</summary>
public sealed record DonorTagResponse(Guid Id, string Code, string Name, string? Description, string Status);

/// <summary>Who owns the relationship today.</summary>
public sealed record RelationshipOwnerResponse(Guid UserId, string? Name);

/// <summary>The badge at the top of the consent panel.</summary>
public sealed record ConsentStatusResponse(
    string OverallState,
    int GrantedChannelCount,
    int WithdrawnChannelCount,
    DateTimeOffset? LastRecordedAtUtc,
    string? NoticeVersion);

/// <summary>One channel and what the donor said about it.</summary>
public sealed record CommunicationPreferenceResponse(
    string Channel,
    string ConsentState,
    string Status,
    DateTimeOffset? EffectiveAtUtc,
    DateTimeOffset? ExpiryAtUtc,
    bool PublicRecognitionPreference);

/// <summary>
/// One row of "Donation totals by stage". Carries its own cut-off and freshness because the
/// numbers are owned by another section.
/// </summary>
public sealed record DonationTotalResponse(
    string Stage,
    string Currency,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset AsAtUtc,
    DateTimeOffset RefreshedAtUtc,
    string SourceFreshness);

/// <summary>One campaign the donor arrived through.</summary>
public sealed record CampaignHistoryResponse(
    Guid CampaignId,
    string CampaignCode,
    string CampaignName,
    string LeadReference,
    DateTimeOffset? ConvertedAtUtc);

/// <summary>One conversation from the interaction log.</summary>
public sealed record ConversationResponse(
    Guid Id,
    string Name,
    string? Description,
    string InteractionType,
    string? Channel,
    DateTimeOffset OccurredAtUtc,
    string Outcome,
    string? PerformedByName,
    string Status);

/// <summary>One open follow-up, shown on the 360 view rather than only on the planner.</summary>
public sealed record Donor360FollowUpResponse(
    Guid Id,
    string FollowUpReference,
    string? NextAction,
    DateTimeOffset? DueAtUtc,
    string Priority,
    string Status,
    string? RelationshipOwnerName);

/// <summary>One pledge.</summary>
public sealed record PromiseResponse(
    Guid Id,
    string Reference,
    decimal Amount,
    string Currency,
    DateTimeOffset PromisedAtUtc,
    DateTimeOffset? DueAtUtc,
    string Status,
    string? CampaignName);

/// <summary>One linked document. Confidential rows never reach an unpermitted caller.</summary>
public sealed record DocumentResponse(
    Guid Id,
    string Reference,
    string Name,
    string? Description,
    string Classification,
    string? ScanStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>One duplicate review that mentions this donor.</summary>
public sealed record DuplicateLinkResponse(
    Guid MergeCaseId,
    string ReviewReference,
    string Status,
    string IdentityConfidence,
    string? Decision,
    string ComparisonRoute);

/// <summary>One row of the audit-relevant chronology.</summary>
public sealed record ActivityHistoryResponse(
    Guid Id,
    string ActionCode,
    string TargetType,
    string Result,
    string? Reason,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

/// <summary>SCR-DON-003 Create intent body. Records a stated giving intention as a promise.</summary>
public sealed class CreateIntentRequest
{
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "INR";

    public DateTimeOffset? DueAtUtc { get; set; }

    public Guid? CampaignId { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string Notes { get; set; } = string.Empty;
}
