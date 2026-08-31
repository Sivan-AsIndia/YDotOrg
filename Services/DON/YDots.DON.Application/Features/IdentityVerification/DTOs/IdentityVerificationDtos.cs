using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Features.IdentityVerification.DTOs;

/// <summary>GET /api/v1/donors/donor-identity-verification. Attempts plus every filter option.</summary>
public sealed record IdentityVerificationListResponse(
    string ScreenId,
    string Route,
    PagedResponse<IdentityVerificationResponse> Verifications,
    IReadOnlyList<LookupItem> ChannelOptions,
    IReadOnlyList<LookupItem> StatusOptions,
    IReadOnlyList<LookupItem> ConfidenceOptions,
    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    int CodeValidMinutes,
    int MaximumAttempts,
    string State);

/// <summary>
/// One verification attempt. The destination is only ever the masked form, and the code
/// itself never appears in a response at all.
/// </summary>
public sealed record IdentityVerificationResponse(
    Guid Id,
    string VerificationReference,
    Guid DonorId,
    string? DonorReference,
    string? DonorDisplayName,
    string? VerificationPurpose,
    string VerificationChannel,
    string? MaskedDestination,
    string Status,
    int AttemptCount,
    int RemainingAttempts,
    DateTimeOffset? ExpiryAtUtc,
    string IdentityConfidence,
    string? EvidenceReference,
    Guid? ReviewerUserId,
    string? ReviewerName,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    string? EscalationReason,
    string? CancellationReason,
    DateTimeOffset CreatedAtUtc,
    long Version,
    bool IsEvidenceMasked,
    IReadOnlyList<string> PermittedActions);

/// <summary>POST .../send-challenge. Starts a verification and delivers the code.</summary>
public sealed class SendChallengeRequest
{
    public Guid DonorId { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string VerificationPurpose { get; set; } = string.Empty;

    /// <summary>Email, Sms, WhatsApp or PhoneCall.</summary>
    public string VerificationChannel { get; set; } = string.Empty;
}

/// <summary>POST .../verify-code. Checks the code the donor read back.</summary>
public sealed class VerifyCodeRequest
{
    public string Code { get; set; } = string.Empty;

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../escalate-review. Hands the attempt to a named reviewer.</summary>
public sealed class EscalateVerificationRequest
{
    public Guid ReviewerUserId { get; set; }

    public string ReviewerName { get; set; } = string.Empty;

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string EscalationReason { get; set; } = string.Empty;

    /// <summary>Stable reference of the supporting evidence document.</summary>
    public string? EvidenceReference { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>
/// What the caller gets back from Send challenge. The code is deliberately absent: it goes to
/// the donor, not to the screen.
/// </summary>
public sealed record ChallengeSentResponse(
    IdentityVerificationResponse Verification,
    string DeliveryStatus,
    string Message,
    string? PendingDependency);
