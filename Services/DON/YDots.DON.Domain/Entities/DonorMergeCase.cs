using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Aggregate root (table don_donor_merge_cases). One duplicate review: two candidates, the
/// evidence behind the match and the steward's decision. This is the record behind SCR-DON-004.
/// </summary>
public class DonorMergeCase : AuditEntity, IOrganisationOwned
{
    // ---- Section 3.5 property contract ---------------------------------------------------

    /// <summary>2 to 160 characters, for example "Possible duplicate - Arun Kumar".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum 2000 characters.</summary>
    public string? Description { get; set; }

    public DonorMergeCaseStatus Status { get; set; } = DonorMergeCaseStatus.Active;

    // ---- Operational columns ---------------------------------------------------------------

    public Guid OrganisationId { get; set; }

    /// <summary>Stable reference shown in the header, for example DUP-2026-000042.</summary>
    public string ReviewReference { get; set; } = string.Empty;

    public Guid CandidateADonorId { get; set; }

    public Donor? CandidateADonor { get; set; }

    public Guid CandidateBDonorId { get; set; }

    public Donor? CandidateBDonor { get; set; }

    /// <summary>Safe side-by-side summary of the two contact sets. Masked unless separately permitted.</summary>
    public string? ContactComparison { get; set; }

    public IdentityConfidence IdentityConfidence { get; set; } = IdentityConfidence.Unknown;

    /// <summary>Why the matcher thinks these are the same person. Confidential.</summary>
    public string? MatchingEvidence { get; set; }

    /// <summary>Fields where the two records disagree, as a readable list.</summary>
    public string? ConflictingFields { get; set; }

    /// <summary>What a merge would do to the donation history of each candidate.</summary>
    public string? DonationHistoryImpact { get; set; }

    /// <summary>What a merge would do to the consent records of each candidate.</summary>
    public string? ConsentImpact { get; set; }

    public MergeDecision? Decision { get; set; }

    /// <summary>Required on every decision. 10 to 2000 characters.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>The record that survives a merge. Must be candidate A or candidate B.</summary>
    public Guid? SurvivingDonorId { get; set; }

    /// <summary>Readable preview of the record that a merge would produce.</summary>
    public string? MergePreview { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }
}
