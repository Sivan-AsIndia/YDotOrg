using YDots.DON.Domain.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.ValueObjects;

namespace YDots.DON.Application.Features.Leads.Mappings;

/// <summary>Manual mapping for the lead capture and lead work queue slices.</summary>
public static class LeadMappingConfig
{
    public static Lead ToEntity(this CreateLeadRequest request, string leadReference, Guid organisationId) =>
        new()
        {
            OrganisationId = organisationId,
            LeadReference = leadReference,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName?.Trim(),
            MobileNumber = NormalisePhone(request.MobileNumber),
            EmailAddress = NormaliseEmail(request.EmailAddress),
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? SupportedLanguages.Default
                : request.PreferredLanguage.Trim(),
            City = request.City?.Trim(),
            GeographyCode = request.GeographyCode?.Trim(),
            CampaignId = request.CampaignId,
            Source = request.Source.Trim(),
            Notes = request.Notes?.Trim(),
            PreferredContactTimeUtc = request.PreferredContactTimeUtc,
            TeamCode = request.TeamCode?.Trim(),
            NextAction = request.NextAction?.Trim(),
            NextActionDueUtc = request.NextActionDueUtc,
            Status = LeadStatus.New,
            ConsentState = request.Consent?.CollectConsent == true ? ConsentState.Granted : ConsentState.NotProvided,
            ConsentEvidenceReference = request.Consent?.ConsentEvidenceReference?.Trim(),
            IsDraft = true
        };

    public static void ApplyUpdate(this UpdateLeadRequest request, Lead lead)
    {
        lead.FirstName = request.FirstName.Trim();
        lead.LastName = request.LastName?.Trim();
        lead.MobileNumber = NormalisePhone(request.MobileNumber);
        lead.EmailAddress = NormaliseEmail(request.EmailAddress);
        lead.PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage)
            ? lead.PreferredLanguage
            : request.PreferredLanguage.Trim();
        lead.City = request.City?.Trim();
        lead.GeographyCode = request.GeographyCode?.Trim();
        lead.CampaignId = request.CampaignId;
        lead.Source = request.Source.Trim();
        lead.Notes = request.Notes?.Trim();
        lead.PreferredContactTimeUtc = request.PreferredContactTimeUtc;
        lead.NextAction = request.NextAction?.Trim();
        lead.NextActionDueUtc = request.NextActionDueUtc;

        if (request.Consent?.CollectConsent == true)
        {
            lead.ConsentState = ConsentState.Granted;
            lead.ConsentEvidenceReference = request.Consent.ConsentEvidenceReference?.Trim();
        }
    }

    /// <summary>
    /// One grid row.
    ///
    /// <paramref name="now"/> IS PASSED IN RATHER THAN READ FROM THE CLOCK because health decays
    /// with time: its recency component would otherwise be whatever it was when the record was
    /// last written, so a lead nobody has touched for a month would still show the score it had
    /// the day somebody last saved it. Taking the instant from the caller also keeps every row on
    /// one page scored against the same moment.
    ///
    /// THE CONTACT COLUMNS OBEY THE SAME MASK as the combined preview - <c>ContactMasking</c> is
    /// given <paramref name="canSeeContact"/> exactly as before. Splitting one masked string into
    /// three fields would be a data leak if the split forgot the mask, so neither branch here
    /// touches the raw value directly.
    /// </summary>
    public static LeadListItemResponse ToListItemResponse(
        this Lead lead,
        bool canSeeContact,
        DateTimeOffset now) =>
        new(
            lead.Id,
            lead.LeadReference,
            BuildContactPreview(lead, canSeeContact),
            BuildDisplayName(lead),
            ContactMasking.Phone(lead.MobileNumber, canSeeContact),
            ContactMasking.Email(lead.EmailAddress, canSeeContact),
            lead.Campaign?.Name,
            lead.OwnerUserId,
            lead.OwnerName,
            lead.Status.ToString(),
            lead.Source,
            lead.Temperature.ToString(),
            lead.DonationPotential.ToString(),
            LeadHealth.Calculate(lead, now),
            lead.NextAction,
            lead.NextActionDueUtc,
            lead.SlaState.ToString(),
            lead.LastContactOutcome.ToString(),
            lead.PreferredLanguage,
            lead.Status == LeadStatus.Converted || lead.ConvertedDonorId is not null,
            lead.ConvertedDonorId,
            lead.UpdatedAtUtc ?? lead.CreatedAtUtc,
            lead.Version,
            !canSeeContact,
            PermittedActionsFor(lead));

    public static LeadDetailResponse ToDetailResponse(
        this Lead lead,
        bool canSeeContact,
        bool canSeeEvidence,
        IReadOnlyList<Consent> consents) =>
        new(
            lead.Id,
            lead.LeadReference,
            lead.FirstName,
            lead.LastName,
            ContactMasking.Phone(lead.MobileNumber, canSeeContact),
            ContactMasking.Email(lead.EmailAddress, canSeeContact),
            lead.PreferredLanguage,
            lead.City,
            lead.GeographyCode,
            lead.CampaignId,
            lead.Campaign?.Name,
            lead.Source,
            lead.ConsentState.ToString(),
            ContactMasking.Confidential(lead.ConsentEvidenceReference, canSeeEvidence),
            ContactMasking.Confidential(lead.Notes, canSeeEvidence),
            lead.PreferredContactTimeUtc,
            lead.DuplicateCandidateSummary,
            lead.Status.ToString(),
            lead.OwnerUserId,
            lead.OwnerName,
            lead.TeamCode,
            lead.NextAction,
            lead.NextActionDueUtc,
            lead.SlaState.ToString(),
            lead.LastContactOutcome.ToString(),
            lead.LastContactedAtUtc,
            lead.AcceptedAtUtc,
            lead.QualifiedAtUtc,
            lead.ConvertedDonorId,
            lead.ConvertedAtUtc,
            lead.ClosureReason,
            lead.IsDraft,
            lead.CreatedAtUtc,
            lead.CreatedByUserId,
            lead.UpdatedAtUtc,
            lead.UpdatedByUserId,
            lead.Version,
            !canSeeContact,
            !canSeeEvidence,
            [.. consents.Select(ToConsentSummary)],
            PermittedActionsFor(lead));

    public static LeadConsentSummaryResponse ToConsentSummary(Consent consent) =>
        new(
            consent.Id,
            consent.Channel.ToString(),
            consent.ConsentState.ToString(),
            consent.Status.ToString(),
            consent.NoticeVersion,
            consent.EffectiveAtUtc,
            consent.ExpiryAtUtc);

    public static LeadLookupResponse ToLookupResponse(this Lead lead) =>
        new(lead.Id, lead.LeadReference, BuildDisplayName(lead), lead.Status.ToString());

    /// <summary>
    /// Which queue actions the lead's current state allows. UI section 5.5: a state moves only
    /// through a named action, so the list here is the state machine the buttons follow.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(Lead lead) =>
        lead.Status switch
        {
            LeadStatus.New => ["Accept", "Assign", "Contact", "Close"],
            LeadStatus.Assigned => ["Contact", "Assign", "Qualify", "Close"],
            LeadStatus.Contacted => ["Qualify", "Contact", "Assign", "Close"],
            LeadStatus.Qualified => ["Convert", "Contact", "Assign", "Close"],
            LeadStatus.Nurture => ["Contact", "Assign", "Qualify", "Close"],
            LeadStatus.Converted => ["Open donor"],
            LeadStatus.Closed => ["View"],
            LeadStatus.Suppressed => ["View"],
            _ => ["View"]
        };

    /// <summary>
    /// The SLA badge. Derived from the due date every time it is read rather than stored and
    /// left to go stale, because "overdue" changes without anybody touching the record.
    /// </summary>
    public static SlaState CalculateSlaState(DateTimeOffset? dueAtUtc, DateTimeOffset now, DonorSettings settings)
    {
        if (dueAtUtc is null)
        {
            return SlaState.NotApplicable;
        }

        var hoursRemaining = (dueAtUtc.Value - now).TotalHours;

        return hoursRemaining switch
        {
            _ when hoursRemaining < -settings.SlaBreachHours => SlaState.Breached,
            _ when hoursRemaining < 0 => SlaState.Overdue,
            _ when hoursRemaining <= settings.SlaDueSoonHours => SlaState.DueToday,
            _ => SlaState.OnTrack
        };
    }

    /// <summary>Open-work count to workload band, using the configured thresholds.</summary>
    public static WorkloadBand CalculateWorkloadBand(int openWorkCount, DonorSettings settings) =>
        openWorkCount switch
        {
            _ when openWorkCount <= settings.WorkloadLightThreshold => WorkloadBand.Light,
            _ when openWorkCount <= settings.WorkloadBalancedThreshold => WorkloadBand.Balanced,
            _ when openWorkCount <= settings.WorkloadHeavyThreshold => WorkloadBand.Heavy,
            _ => WorkloadBand.Overloaded
        };

    public static string BuildDisplayName(Lead lead) =>
        string.Join(' ', new[] { lead.FirstName, lead.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>
    /// "Name and contact preview" for the grid. The name is internal, the contact detail is
    /// restricted, so the preview shows the name in full and the contact masked unless the
    /// caller holds don.donors.view-sensitive-contact.
    /// </summary>
    private static string BuildContactPreview(Lead lead, bool canSeeContact)
    {
        var name = BuildDisplayName(lead);
        var contact = !string.IsNullOrWhiteSpace(lead.MobileNumber)
            ? ContactMasking.Phone(lead.MobileNumber, canSeeContact)
            : ContactMasking.Email(lead.EmailAddress, canSeeContact);

        return string.IsNullOrWhiteSpace(contact) ? name : $"{name} · {contact}";
    }

    private static string? NormaliseEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : EmailValue.TryParse(value)?.Value ?? value.Trim().ToLowerInvariant();

    private static string? NormalisePhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : PrimaryPhoneValue.TryParse(value)?.Value ?? value.Trim();
}
