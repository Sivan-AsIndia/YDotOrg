using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.ConsentCentre.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.ConsentCentre.Mappings;

/// <summary>Manual mapping for the consent and preference centre.</summary>
public static class ConsentMappingConfig
{
    public static ConsentListItemResponse ToListItemResponse(this Consent consent, bool canSeeEvidence) =>
        new(
            consent.Id,
            consent.DonorId,
            consent.Donor?.DonorNumber,
            consent.Donor?.DisplayName,
            consent.LeadId,
            consent.Name,
            consent.Description,
            consent.Purpose,
            consent.Channel.ToString(),
            consent.ConsentState.ToString(),
            consent.Status.ToString(),
            consent.NoticeVersion,
            consent.EvidenceSource,
            ContactMasking.Confidential(consent.EvidenceReference, canSeeEvidence),
            consent.EffectiveAtUtc,
            consent.ExpiryAtUtc,
            consent.PublicRecognitionPreference,
            ContactMasking.Phone(consent.ContactRestrictions, canSeeEvidence),
            ContactMasking.Confidential(consent.CorrectionReason, canSeeEvidence),
            consent.SupersededByConsentId,
            consent.WithdrawnAtUtc,
            consent.WithdrawalReason,
            consent.CapturedByName,
            consent.CreatedAtUtc,
            consent.Version,
            !canSeeEvidence,
            PermittedActionsFor(consent));

    /// <summary>
    /// Which actions a consent row allows. A superseded or expired row is history: it can be
    /// read but never changed, because rewriting it would break the audit trail it exists for.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(Consent consent) =>
        consent.Status switch
        {
            ConsentStatus.Active => ["Review evidence", "Withdraw", "Correct"],
            ConsentStatus.Withdrawn => ["Review evidence", "Grant"],
            _ => ["Review evidence"]
        };

    /// <summary>
    /// Builds the corrected copy of a consent row. Everything the caller did not supply is
    /// carried forward from the original, so a correction to one field cannot silently blank
    /// the others.
    /// </summary>
    public static Consent ToCorrectedCopy(this Consent original, CorrectConsentRequest request, string noticeVersion) =>
        new()
        {
            DonorId = original.DonorId,
            LeadId = original.LeadId,
            OrganisationId = original.OrganisationId,
            Name = original.Name,
            Description = original.Description,
            Status = original.Status,
            Purpose = request.Purpose?.Trim() ?? original.Purpose,
            Channel = original.Channel,
            ConsentState = original.ConsentState,
            NoticeVersion = noticeVersion,
            EvidenceSource = request.EvidenceSource?.Trim() ?? original.EvidenceSource,
            EvidenceReference = request.EvidenceReference?.Trim() ?? original.EvidenceReference,
            EffectiveAtUtc = request.EffectiveAtUtc ?? original.EffectiveAtUtc,
            ExpiryAtUtc = request.ExpiryAtUtc ?? original.ExpiryAtUtc,
            PublicRecognitionPreference = request.PublicRecognitionPreference ?? original.PublicRecognitionPreference,
            ContactRestrictions = request.ContactRestrictions?.Trim() ?? original.ContactRestrictions,
            CorrectionReason = request.CorrectionReason.Trim()
        };
}
