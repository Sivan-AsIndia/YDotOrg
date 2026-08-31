using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.ValueObjects;

namespace YDots.DON.Application.Features.Donors.Mappings;

/// <summary>
/// Manual mapping for the Donor slice. Plain extension methods rather than a mapping library:
/// the rules are visible, they are debuggable, and nothing is discovered at run time.
/// </summary>
public static class DonorMappingConfig
{
    /// <summary>Request to new aggregate. The caller never sets identity, audit or status columns.</summary>
    public static Donor ToEntity(this CreateDonorRequest request, string donorNumber, Guid organisationId) =>
        new()
        {
            DonorNumber = donorNumber,
            DonorType = request.DonorType,
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            OrganisationName = request.OrganisationName?.Trim(),
            PrimaryEmail = NormaliseEmail(request.PrimaryEmail),
            PrimaryPhone = NormalisePhone(request.PrimaryPhone),
            PreferredLanguage = request.PreferredLanguage.Trim(),
            DoNotContact = request.DoNotContact,
            Status = DonorStatus.Prospect,
            ApprovalState = ApprovalState.NotSubmitted,
            OrganisationId = organisationId,
            NormalizedBusinessKey = BuildBusinessKey(
                request.PrimaryEmail,
                request.PrimaryPhone,
                request.DonorType,
                request.FirstName,
                request.LastName,
                request.OrganisationName)
        };

    /// <summary>Applies an edit in place. The version bump is the DbContext's job, not ours.</summary>
    public static void ApplyUpdate(this UpdateDonorRequest request, Donor donor)
    {
        donor.DonorType = request.DonorType;
        donor.FirstName = request.FirstName?.Trim();
        donor.LastName = request.LastName?.Trim();
        donor.OrganisationName = request.OrganisationName?.Trim();
        donor.PrimaryEmail = NormaliseEmail(request.PrimaryEmail);
        donor.PrimaryPhone = NormalisePhone(request.PrimaryPhone);
        donor.PreferredLanguage = request.PreferredLanguage.Trim();
        donor.DoNotContact = request.DoNotContact;
        donor.NormalizedBusinessKey = BuildBusinessKey(
            request.PrimaryEmail,
            request.PrimaryPhone,
            request.DonorType,
            request.FirstName,
            request.LastName,
            request.OrganisationName);
    }

    public static DonorListItemResponse ToListItemResponse(this Donor donor) =>
        new(
            donor.Id,
            donor.DonorNumber,
            donor.DisplayName,
            donor.Status.ToString(),
            donor.RelationshipOwnerUserId,
            donor.RelationshipOwnerName,
            donor.UpdatedAtUtc ?? donor.CreatedAtUtc,
            donor.Version);

    public static DonorLookupResponse ToLookupResponse(this Donor donor) =>
        new(donor.Id, donor.DisplayName, donor.Status.ToString());

    /// <summary>
    /// Full detail. <paramref name="canSeeContact"/> comes from don.donors.view-sensitive-contact
    /// and decides whether the e-mail and phone leave the server in the clear.
    /// </summary>
    public static DonorDetailResponse ToDetailResponse(
        this Donor donor,
        bool canSeeContact,
        IReadOnlyList<string> permittedActions) =>
        new(
            donor.Id,
            donor.CreatedAtUtc,
            donor.CreatedByUserId,
            donor.UpdatedAtUtc,
            donor.UpdatedByUserId,
            donor.Version,
            donor.DonorNumber,
            donor.DonorType,
            donor.FirstName,
            donor.LastName,
            donor.OrganisationName,
            ContactMasking.Email(donor.PrimaryEmail, canSeeContact),
            ContactMasking.Phone(donor.PrimaryPhone, canSeeContact),
            donor.PreferredLanguage,
            donor.Status,
            donor.DoNotContact,
            donor.DisplayName,
            donor.ApprovalState.ToString(),
            donor.RelationshipOwnerUserId,
            donor.RelationshipOwnerName,
            donor.Notes,
            !canSeeContact && !string.IsNullOrWhiteSpace(donor.PrimaryEmail),
            !canSeeContact && !string.IsNullOrWhiteSpace(donor.PrimaryPhone),
            permittedActions);

    /// <summary>
    /// Which actions the record's own state allows. Permission is checked separately by
    /// [HasPermission] on the endpoint; this list is what the UI uses to decide what to draw.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(Donor donor) =>
        donor.Status switch
        {
            DonorStatus.Archived => ["View"],
            DonorStatus.Merged => ["View"],
            DonorStatus.Restricted => ["View", "Edit", "Archive"],
            DonorStatus.Prospect when donor.ApprovalState == ApprovalState.PendingApproval =>
                ["View", "Approve", "Cancel"],
            DonorStatus.Prospect => ["View", "Edit", "Submit", "Cancel"],
            DonorStatus.Active => ["View", "Edit", "Correct", "Follow up", "Cancel", "Archive"],
            _ => ["View"]
        };

    /// <summary>
    /// The natural key behind ExistsByBusinessKeyAsync and the DUPLICATE_RECORD check.
    /// E-mail first because it is the strongest signal, then phone, then the display name.
    /// </summary>
    public static string BuildBusinessKey(
        string? email,
        string? phone,
        DonorType donorType,
        string? firstName,
        string? lastName,
        string? organisationName)
    {
        var normalisedEmail = NormaliseEmail(email);
        if (!string.IsNullOrWhiteSpace(normalisedEmail))
        {
            return "email:" + normalisedEmail;
        }

        var normalisedPhone = NormalisePhone(phone);
        if (!string.IsNullOrWhiteSpace(normalisedPhone))
        {
            return "phone:" + normalisedPhone;
        }

        var name = donorType == DonorType.Organisation
            ? organisationName
            : string.Join(' ', new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return "name:" + (name ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string? NormaliseEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : EmailValue.TryParse(value)?.Value ?? value.Trim().ToLowerInvariant();

    private static string? NormalisePhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : PrimaryPhoneValue.TryParse(value)?.Value ?? value.Trim();
}
