using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Immutable, append-only record (table don_consents). A correction never edits a row: it
/// marks the previous one Superseded and inserts a new one, which is what makes the consent
/// history in SCR-DON-005 defensible.
/// </summary>
public class Consent : AuditEntity, IOrganisationOwned
{
    // ---- Section 3.3 property contract ---------------------------------------------------

    /// <summary>2 to 160 characters, for example "Email consent - fundraising updates".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum 2000 characters.</summary>
    public string? Description { get; set; }

    public ConsentStatus Status { get; set; } = ConsentStatus.Active;

    // ---- Operational columns ---------------------------------------------------------------

    public Guid? DonorId { get; set; }

    public Donor? Donor { get; set; }

    /// <summary>Consent captured on the lead-capture screen before the donor record exists.</summary>
    public Guid? LeadId { get; set; }

    public Guid OrganisationId { get; set; }

    /// <summary>Why the permission is being asked for. 10 to 2000 characters.</summary>
    public string Purpose { get; set; } = string.Empty;

    public ConsentChannel Channel { get; set; } = ConsentChannel.Email;

    public ConsentState ConsentState { get; set; } = ConsentState.NotProvided;

    /// <summary>Version of the privacy notice that was shown, for example "PN-2026-01".</summary>
    public string NoticeVersion { get; set; } = string.Empty;

    /// <summary>Where the evidence came from: web form, call recording, signed paper form.</summary>
    public string EvidenceSource { get; set; } = string.Empty;

    /// <summary>Stable reference of the uploaded or linked evidence document.</summary>
    public string? EvidenceReference { get; set; }

    public DateTimeOffset EffectiveAtUtc { get; set; }

    public DateTimeOffset? ExpiryAtUtc { get; set; }

    /// <summary>Whether the donor allows public recognition. Published only through an approved field.</summary>
    public bool PublicRecognitionPreference { get; set; }

    /// <summary>Numbers or times the donor asked not to be contacted on.</summary>
    public string? ContactRestrictions { get; set; }

    /// <summary>Required when this row was created by the Correct action. 10 to 2000 characters.</summary>
    public string? CorrectionReason { get; set; }

    /// <summary>Points at the row that replaced this one.</summary>
    public Guid? SupersededByConsentId { get; set; }

    public DateTimeOffset? WithdrawnAtUtc { get; set; }

    public string? WithdrawalReason { get; set; }

    public Guid CapturedByUserId { get; set; }

    public string? CapturedByName { get; set; }
}
