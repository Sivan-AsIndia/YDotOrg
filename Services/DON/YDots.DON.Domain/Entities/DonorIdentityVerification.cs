using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// One identity verification attempt (table don_donor_identity_verifications). The record
/// behind DON-UI-07: prove that the person on the other end really owns the contact detail
/// before a sensitive correction, a merge or portal access is allowed.
/// </summary>
public class DonorIdentityVerification : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    /// <summary>Stable reference, for example VER-2026-000091.</summary>
    public string VerificationReference { get; set; } = string.Empty;

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    /// <summary>Why the verification is being run. 10 to 2000 characters.</summary>
    public string? VerificationPurpose { get; set; }

    public VerificationChannel VerificationChannel { get; set; } = VerificationChannel.Sms;

    /// <summary>
    /// What the caller is shown, for example "+91******3210". The full destination is never
    /// stored on this row and never returned to the browser.
    /// </summary>
    public string? MaskedDestination { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.NotStarted;

    public int AttemptCount { get; set; }

    public DateTimeOffset? ExpiryAtUtc { get; set; }

    public IdentityConfidence IdentityConfidence { get; set; } = IdentityConfidence.Unknown;

    /// <summary>Stable reference of the supporting evidence document. Confidential.</summary>
    public string? EvidenceReference { get; set; }

    public Guid? ReviewerUserId { get; set; }

    public string? ReviewerName { get; set; }

    /// <summary>
    /// Hash of the code that was sent. The plain code is delivered to the donor and never kept,
    /// so a database reader cannot pass somebody else's challenge.
    /// </summary>
    public string? ChallengeCodeHash { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public string? EscalationReason { get; set; }

    public string? CancellationReason { get; set; }
}
