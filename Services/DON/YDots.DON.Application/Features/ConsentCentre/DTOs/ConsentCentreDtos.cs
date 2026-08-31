using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Features.ConsentCentre.DTOs;

/// <summary>
/// GET /api/v1/donors/consent-and-preference-centre. Current rows, the full history and every
/// catalogue the form needs, in one call.
/// </summary>
public sealed record ConsentCentreResponse(
    string ScreenId,
    string Route,
    PagedResponse<ConsentListItemResponse> Consents,
    IReadOnlyList<ConsentListItemResponse> ConsentHistory,
    IReadOnlyList<LookupItem> ChannelOptions,
    IReadOnlyList<LookupItem> ConsentStateOptions,
    IReadOnlyList<LookupItem> StatusOptions,
    string CurrentNoticeVersion,
    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    string State);

/// <summary>One consent row. Evidence is confidential and arrives masked by default.</summary>
public sealed record ConsentListItemResponse(
    Guid Id,
    Guid? DonorId,
    string? DonorReference,
    string? DonorDisplayName,
    Guid? LeadId,
    string Name,
    string? Description,
    string Purpose,
    string Channel,
    string ConsentState,
    string Status,
    string NoticeVersion,
    string EvidenceSource,
    string? EvidenceReference,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset? ExpiryAtUtc,
    bool PublicRecognitionPreference,
    string? ContactRestrictions,
    string? CorrectionReason,
    Guid? SupersededByConsentId,
    DateTimeOffset? WithdrawnAtUtc,
    string? WithdrawalReason,
    string? CapturedByName,
    DateTimeOffset CreatedAtUtc,
    long Version,
    bool IsEvidenceMasked,
    IReadOnlyList<string> PermittedActions);

/// <summary>POST .../grant. Records a new permission for one channel.</summary>
public sealed class GrantConsentRequest
{
    public Guid DonorId { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Required. From the ConsentChannel catalogue.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Required. Where the evidence came from.</summary>
    public string EvidenceSource { get; set; } = string.Empty;

    /// <summary>Stable reference of the uploaded evidence document.</summary>
    public string? EvidenceReference { get; set; }

    /// <summary>Required.</summary>
    public DateTimeOffset EffectiveAtUtc { get; set; }

    public DateTimeOffset? ExpiryAtUtc { get; set; }

    public bool PublicRecognitionPreference { get; set; }

    public string? ContactRestrictions { get; set; }

    public string? Description { get; set; }
}

/// <summary>POST .../withdraw. Closes the current permission for one channel.</summary>
public sealed class WithdrawConsentRequest
{
    /// <summary>Required. 10 to 2000 characters.</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset? EffectiveAtUtc { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>
/// POST .../correct. Supersedes the existing row with a corrected copy. Nothing is overwritten,
/// which is what makes the consent history defensible.
/// </summary>
public sealed class CorrectConsentRequest
{
    /// <summary>Required. 10 to 2000 characters.</summary>
    public string CorrectionReason { get; set; } = string.Empty;

    public string? Purpose { get; set; }

    public string? EvidenceSource { get; set; }

    public string? EvidenceReference { get; set; }

    public DateTimeOffset? EffectiveAtUtc { get; set; }

    public DateTimeOffset? ExpiryAtUtc { get; set; }

    public bool? PublicRecognitionPreference { get; set; }

    public string? ContactRestrictions { get; set; }

    public long? ExpectedVersion { get; set; }
}
