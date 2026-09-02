using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Features.CommunicationTimeline.DTOs;

/// <summary>
/// One line of the timeline: something that was said to, or heard from, this person.
///
/// IT COVERS BOTH SIDES OF THE CONVERSION. The Donors and Leads document is explicit that a
/// converted donor "retains the existing owner and Communication Timeline history", so the
/// timeline is keyed by the LEAD as well as the donor - an interaction recorded while the record
/// was still a lead must still appear after it becomes a donor, or the history the document
/// promises to preserve disappears at exactly the moment it becomes most useful.
/// </summary>
public sealed record CommunicationTimelineEntryResponse(
    Guid Id,
    string InteractionType,
    string? Channel,
    string Direction,
    DateTimeOffset OccurredAtUtc,
    string Outcome,
    string Summary,

    /// <summary>
    /// The longer note, MASKED unless the caller holds don.donors.view-sensitive-contact.
    ///
    /// A call note routinely contains what a donor said about their circumstances, which is the
    /// most sensitive thing on this screen and the least obviously so.
    /// </summary>
    string? Notes,

    string? PerformedByName,
    bool IsNotesMasked);

/// <summary>
/// The Communication Timeline for one lead or donor.
///
/// PROFILE AND HISTORY IN ONE CALL, because the screen shows them side by side and two calls
/// would let the header and the timeline disagree about who is being looked at.
/// </summary>
public sealed record CommunicationTimelineResponse(
    string ScreenId,
    string Route,

    /// <summary>The lead this timeline belongs to, when it is a lead's.</summary>
    Guid? LeadId,
    string? LeadReference,

    /// <summary>The donor it belongs to, once the lead has converted.</summary>
    Guid? DonorId,
    string? DonorReference,

    string DisplayName,

    /// <summary>Masked on the same rule as everywhere else in the module.</summary>
    string? MobileNumber,
    string? EmailAddress,

    string? CampaignName,
    string? Source,
    string PreferredLanguage,
    string? OwnerName,
    string Status,
    string Temperature,
    string DonationPotential,
    int HealthScore,

    IReadOnlyList<CommunicationTimelineEntryResponse> Entries,

    /// <summary>Cold/Warm/Hot and Low/Medium/High, for the two update dialogs.</summary>
    IReadOnlyList<LookupItem> TemperatureOptions,
    IReadOnlyList<LookupItem> DonationPotentialOptions,
    IReadOnlyList<LookupItem> InteractionTypeOptions,
    IReadOnlyList<LookupItem> OutcomeOptions,

    IReadOnlyList<string> PermittedActions,
    bool IsContactMasked,
    string ActiveScope,
    string State);
