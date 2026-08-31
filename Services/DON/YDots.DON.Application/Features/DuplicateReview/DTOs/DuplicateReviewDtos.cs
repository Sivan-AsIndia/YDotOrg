using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Features.DuplicateReview.DTOs;

/// <summary>GET /api/v1/donors/duplicate-review. The review queue plus its filter options.</summary>
public sealed record DuplicateReviewListResponse(
    string ScreenId,
    string Route,
    PagedResponse<DuplicateReviewListItemResponse> Reviews,
    IReadOnlyList<LookupItem> StatusOptions,
    IReadOnlyList<LookupItem> ConfidenceOptions,
    IReadOnlyList<LookupItem> DecisionOptions,
    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    string State);

/// <summary>One row of the review queue.</summary>
public sealed record DuplicateReviewListItemResponse(
    Guid Id,
    string ReviewReference,
    string Name,
    string CandidateAName,
    string CandidateBName,
    string IdentityConfidence,
    string Status,
    string? Decision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    long Version);

/// <summary>
/// The full comparison behind SCR-DON-004. Contact comparison is restricted and matching
/// evidence is confidential, so both arrive masked unless the caller is separately permitted.
/// </summary>
public sealed record DuplicateReviewDetailResponse(
    Guid Id,
    string ReviewReference,
    string Name,
    string? Description,
    string Status,
    CandidateSummaryResponse CandidateA,
    CandidateSummaryResponse CandidateB,
    string? ContactComparison,
    string IdentityConfidence,
    string? MatchingEvidence,
    string? ConflictingFields,
    string? DonationHistoryImpact,
    string? ConsentImpact,
    string? Decision,
    string? DecisionReason,
    Guid? SurvivingDonorId,
    string? MergePreview,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    long Version,
    bool IsContactComparisonMasked,
    bool IsEvidenceMasked,
    IReadOnlyList<string> PermittedActions);

/// <summary>One side of the comparison. Safe values only: reference, name, status.</summary>
public sealed record CandidateSummaryResponse(
    Guid DonorId,
    string DonorNumber,
    string DisplayName,
    string DonorType,
    string Status,
    string PreferredLanguage,
    DateTimeOffset CreatedAtUtc,
    string? MaskedEmail,
    string? MaskedPhone);

/// <summary>POST .../duplicate-review. Raise a review for two candidates.</summary>
public sealed class CreateDuplicateReviewRequest
{
    public Guid CandidateADonorId { get; set; }

    public Guid CandidateBDonorId { get; set; }

    /// <summary>2 to 160 characters.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Low, Medium or High.</summary>
    public string? IdentityConfidence { get; set; }

    public string? MatchingEvidence { get; set; }
}

/// <summary>POST .../{id}/merge. The steward's decision, with the surviving record named.</summary>
public sealed class MergeDecisionRequest
{
    /// <summary>Merge, Link or KeepSeparate.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string DecisionReason { get; set; } = string.Empty;

    /// <summary>Required when the decision is Merge. Must be candidate A or candidate B.</summary>
    public Guid? SurvivingDonorId { get; set; }

    public long? ExpectedVersion { get; set; }
}
