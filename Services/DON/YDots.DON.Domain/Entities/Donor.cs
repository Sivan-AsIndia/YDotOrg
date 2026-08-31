using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Aggregate root (table don_donors). The first block is the property contract from
/// section 3.1 of the developer specification, word for word. The second block holds the
/// operational columns the six screens and the submit / approve / cancel / archive
/// lifecycle need; they are additions, never replacements.
/// </summary>
public class Donor : AuditEntity, IOrganisationOwned
{
    // ---- Section 3.1 property contract ---------------------------------------------------

    /// <summary>System generated and unique, for example DON-2026-000184.</summary>
    public string DonorNumber { get; set; } = string.Empty;

    public DonorType DonorType { get; set; } = DonorType.Individual;

    /// <summary>Required for an Individual donor.</summary>
    public string? FirstName { get; set; }

    /// <summary>Required for an Individual donor.</summary>
    public string? LastName { get; set; }

    /// <summary>Required for an Organisation donor.</summary>
    public string? OrganisationName { get; set; }

    public string? PrimaryEmail { get; set; }

    /// <summary>E.164, for example +919876543210.</summary>
    public string? PrimaryPhone { get; set; }

    public string PreferredLanguage { get; set; } = "en-IN";

    public DonorStatus Status { get; set; } = DonorStatus.Prospect;

    /// <summary>Overrides every channel preference when true.</summary>
    public bool DoNotContact { get; set; }

    // ---- Operational columns ---------------------------------------------------------------

    /// <summary>Data scope boundary. Comes from the organisation_id claim in the access token.</summary>
    public Guid OrganisationId { get; set; }

    /// <summary>Maker / checker position. See <see cref="Enums.ApprovalState"/>.</summary>
    public ApprovalState ApprovalState { get; set; } = ApprovalState.NotSubmitted;

    /// <summary>IAM user who owns the relationship. Referenced by Guid only, never joined.</summary>
    public Guid? RelationshipOwnerUserId { get; set; }

    /// <summary>Display name captured when the owner was assigned, so the grid needs no cross-service call.</summary>
    public string? RelationshipOwnerName { get; set; }

    /// <summary>The lead this donor was converted from, when there was one.</summary>
    public Guid? SourceLeadId { get; set; }

    /// <summary>Set when a duplicate review merged this record into a surviving donor.</summary>
    public Guid? MergedIntoDonorId { get; set; }

    /// <summary>
    /// Lower-cased natural key used by the duplicate check: e-mail, else phone, else the
    /// display name. Stored so the uniqueness query is an index seek, not a scan.
    /// </summary>
    public string NormalizedBusinessKey { get; set; } = string.Empty;

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public string? CancellationReason { get; set; }

    public string? ArchiveReason { get; set; }

    /// <summary>Free-text note kept by the Donor 360 correct action.</summary>
    public string? Notes { get; set; }

    // ---- Owned children ----------------------------------------------------------------------

    public ICollection<DonorContact> Contacts { get; set; } = [];

    public ICollection<Consent> Consents { get; set; } = [];

    public ICollection<DonorInteraction> Interactions { get; set; } = [];

    public ICollection<DonorTag> Tags { get; set; } = [];

    /// <summary>"Arun Kumar" for an individual, the organisation name for an organisation.</summary>
    public string DisplayName =>
        DonorType switch
        {
            DonorType.Organisation => OrganisationName ?? DonorNumber,
            DonorType.Anonymous => "Anonymous donor",
            _ => string.Join(' ', new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } name
                ? name
                : DonorNumber
        };
}
