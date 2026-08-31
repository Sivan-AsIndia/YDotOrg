using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// A prospective donor (table don_leads). The record behind SCR-DON-001 lead work queue and
/// SCR-DON-002 lead capture, and the left-hand side of the lead-to-donor conversion flow in
/// UI section 5.
/// </summary>
public class Lead : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    /// <summary>Stable reference shown in the header and in every confirmation, for example LED-2026-000317.</summary>
    public string LeadReference { get; set; } = string.Empty;

    // ---- Personal and contact information (SCR-DON-002 field contract) -----------------------

    /// <summary>"First name or known name". Required.</summary>
    public string FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; }

    /// <summary>E.164. Masked in list, export and support views.</summary>
    public string? MobileNumber { get; set; }

    public string? EmailAddress { get; set; }

    public string PreferredLanguage { get; set; } = "en-IN";

    /// <summary>Free text city, kept exactly as entered.</summary>
    public string? City { get; set; }

    /// <summary>Approved administrative geography code, verified separately from the entered text.</summary>
    public string? GeographyCode { get; set; }

    // ---- Campaign, source and consent context ------------------------------------------------

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>Where the lead came from. Required.</summary>
    public string Source { get; set; } = string.Empty;

    public ConsentState ConsentState { get; set; } = ConsentState.NotProvided;

    /// <summary>Stable reference of the uploaded consent evidence. Confidential.</summary>
    public string? ConsentEvidenceReference { get; set; }

    /// <summary>Confidential note. 10 to 2000 characters when supplied.</summary>
    public string? Notes { get; set; }

    /// <summary>When the person asked to be contacted. Restricted.</summary>
    public DateTimeOffset? PreferredContactTimeUtc { get; set; }

    /// <summary>Server-derived safe summary of possible duplicates, never another person's details.</summary>
    public string? DuplicateCandidateSummary { get; set; }

    // ---- Queue, ownership and lifecycle -------------------------------------------------------

    public LeadStatus Status { get; set; } = LeadStatus.New;

    public Guid? OwnerUserId { get; set; }

    public string? OwnerName { get; set; }

    /// <summary>Team the owner belongs to. Used by the assignment board filter.</summary>
    public string? TeamCode { get; set; }

    public string? NextAction { get; set; }

    public DateTimeOffset? NextActionDueUtc { get; set; }

    public SlaState SlaState { get; set; } = SlaState.NotApplicable;

    public ContactOutcome LastContactOutcome { get; set; } = ContactOutcome.NotContacted;

    public DateTimeOffset? LastContactedAtUtc { get; set; }

    public DateTimeOffset? AcceptedAtUtc { get; set; }

    public DateTimeOffset? QualifiedAtUtc { get; set; }

    /// <summary>Set once Establish relationship has run and a donor record exists.</summary>
    public Guid? ConvertedDonorId { get; set; }

    public DateTimeOffset? ConvertedAtUtc { get; set; }

    /// <summary>Required by the Close action. Preserved for the audit trail.</summary>
    public string? ClosureReason { get; set; }

    /// <summary>True while the record is an unsaved draft that Save has not yet promoted.</summary>
    public bool IsDraft { get; set; } = true;

    public ICollection<LeadAssignment> Assignments { get; set; } = [];
}
