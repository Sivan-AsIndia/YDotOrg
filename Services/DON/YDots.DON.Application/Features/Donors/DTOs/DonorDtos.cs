using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.Donors.DTOs;

/// <summary>
/// POST /api/v1/donors body. Members are exactly the DTO catalogue row for CreateDonorRequest.
///
/// DonorNumber appears here even though the property contract calls it "system generated". The
/// two are reconciled by leaving it optional: send it and an importer keeps its own reference,
/// leave it blank and the server generates the next one in sequence.
/// </summary>
public sealed class CreateDonorRequest
{
    public string? DonorNumber { get; set; }

    public DonorType DonorType { get; set; } = DonorType.Individual;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? OrganisationName { get; set; }

    public string? PrimaryEmail { get; set; }

    public string? PrimaryPhone { get; set; }

    public string PreferredLanguage { get; set; } = "en-IN";

    public bool DoNotContact { get; set; }
}

/// <summary>
/// PUT /api/v1/donors/{id} body. Members are exactly the DTO catalogue row for
/// UpdateDonorRequest. DonorNumber is absent on purpose: the reference never changes.
/// </summary>
public sealed class UpdateDonorRequest
{
    public DonorType DonorType { get; set; } = DonorType.Individual;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? OrganisationName { get; set; }

    public string? PrimaryEmail { get; set; }

    public string? PrimaryPhone { get; set; }

    public string PreferredLanguage { get; set; } = "en-IN";

    public bool DoNotContact { get; set; }

    /// <summary>The version the caller had on screen. A mismatch produces CONCURRENCY_CONFLICT.</summary>
    public long ExpectedVersion { get; set; }
}

/// <summary>
/// One grid row. Intentionally compact, so a hundred-row page stays small and no sensitive
/// value travels to a list view.
///
/// THE RELATIONSHIP OWNER IS ON IT, and its absence used to matter. The grid needs to say who
/// is accountable for a donor - it is the donor list's own Owner filter and the column beside
/// every row - and the only projection carrying it was the detail record, which a list view
/// never fetches. So the workspace filled the column with the literal string "Unassigned" for
/// every donor on the platform, including the ones a conversion had just given an owner.
///
/// It is NOT a sensitive value: the owner is a member of staff in the caller's own scope, the
/// same person the assignment board already names in full.
/// </summary>
public sealed record DonorListItemResponse(
    Guid Id,
    string DisplayCode,
    string DisplayName,
    string Status,
    Guid? RelationshipOwnerUserId,
    string? RelationshipOwnerName,
    DateTimeOffset UpdatedAtUtc,
    long Version,

    /// <summary>
    /// Contact and giving, for the grid the workflow document draws.
    ///
    /// THEY ARE ON THE ROW BECAUSE THE SCREEN SHOWS THEM. The Donor List has columns for mobile,
    /// e-mail, campaign, last donation and lifetime giving; without these the browser would have
    /// to fetch each donor's detail separately to fill a row, which is one request per row on
    /// every page.
    ///
    /// MASKED ON THE SAME RULE AS EVERYWHERE ELSE - don.donors.view-sensitive-contact.
    /// </summary>
    string? MobileNumber,

    string? EmailAddress,

    /// <summary>The campaign the donation that created this donor belonged to.</summary>
    string? CampaignName,

    decimal? LastDonationAmount,
    DateTimeOffset? LastDonationAtUtc,

    /// <summary>Received, summed across every stage-Received summary row.</summary>
    decimal LifetimeGiving,
    string Currency,

    /// <summary>Overdue / DueToday / Tomorrow / None - the follow-up column.</summary>
    string FollowUpStatus,

    /// <summary>Verified / Pending / Failed / Expired.</summary>
    string VerificationStatus,

    /// <summary>Granted / Partial / Withdrawn.</summary>
    string ConsentStatus,

    /// <summary>True when a consent has expired or been withdrawn and needs somebody to look.</summary>
    bool ConsentReviewRequired,

    bool IsContactMasked);

/// <summary>
/// The full detail record. Contact values arrive masked unless the caller holds
/// don.donors.view-sensitive-contact, which is why the two IsMasked flags are here.
/// </summary>
public sealed record DonorDetailResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    string DonorNumber,
    DonorType DonorType,
    string? FirstName,
    string? LastName,
    string? OrganisationName,
    string? PrimaryEmail,
    string? PrimaryPhone,
    string PreferredLanguage,
    DonorStatus Status,
    bool DoNotContact,
    // ---- Supporting values the screens need beside the contract members ----------------
    string DisplayName,
    string ApprovalState,
    Guid? RelationshipOwnerUserId,
    string? RelationshipOwnerName,
    string? Notes,
    bool IsEmailMasked,
    bool IsPhoneMasked,
    IReadOnlyList<string> PermittedActions);

/// <summary>Dropdown and autocomplete row. Three members, as the catalogue states.</summary>
public sealed record DonorLookupResponse(
    Guid Id,
    string DisplayName,
    string Status);
