using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.IdentityVerification.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.IdentityVerification.Mappings;

/// <summary>Manual mapping for DON-UI-07.</summary>
public static class IdentityVerificationMappingConfig
{
    public static IdentityVerificationResponse ToResponse(
        this DonorIdentityVerification verification,
        bool canSeeEvidence,
        int maximumAttempts) =>
        new(
            verification.Id,
            verification.VerificationReference,
            verification.DonorId,
            verification.Donor?.DonorNumber,
            verification.Donor?.DisplayName,
            verification.VerificationPurpose,
            verification.VerificationChannel.ToString(),
            verification.MaskedDestination,
            verification.Status.ToString(),
            verification.AttemptCount,
            Math.Max(0, maximumAttempts - verification.AttemptCount),
            verification.ExpiryAtUtc,
            verification.IdentityConfidence.ToString(),
            ContactMasking.Confidential(verification.EvidenceReference, canSeeEvidence),
            verification.ReviewerUserId,
            verification.ReviewerName,
            verification.SentAtUtc,
            verification.VerifiedAtUtc,
            verification.EscalationReason,
            verification.CancellationReason,
            verification.CreatedAtUtc,
            verification.Version,
            !canSeeEvidence,
            PermittedActionsFor(verification));

    /// <summary>Which actions the attempt state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(DonorIdentityVerification verification) =>
        verification.Status switch
        {
            VerificationStatus.NotStarted => ["Send challenge", "Cancel verification"],
            VerificationStatus.ChallengeSent => ["Verify code", "Escalate review", "Cancel verification", "Send challenge"],
            VerificationStatus.Failed => ["Escalate review", "Send challenge", "Cancel verification"],
            VerificationStatus.Expired => ["Send challenge", "Cancel verification"],
            VerificationStatus.Escalated => ["Verify code", "Cancel verification"],
            _ => ["View"]
        };
}
