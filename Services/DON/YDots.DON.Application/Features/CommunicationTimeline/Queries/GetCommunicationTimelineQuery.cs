using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.Features.CommunicationTimeline.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.Services;

namespace YDots.DON.Application.Features.CommunicationTimeline.Queries;

/// <summary>
/// The Communication Timeline for a lead, or for the donor it became.
///
/// EXACTLY ONE OF THE TWO IDS IS GIVEN. The screen is reached from the Lead Work Queue ("opens
/// the selected lead's Communication Timeline"), from My Leads, from the Follow-Up Queue's View
/// History, and from Donor 360 - the first three hold a lead id and the last a donor id.
/// </summary>
public sealed record GetCommunicationTimelineQuery(Guid? LeadId, Guid? DonorId);

/// <summary>
/// The read side of the Communication Timeline.
///
/// WHY IT READS BY BOTH IDS. The document's conversion rule is that a lead which becomes a donor
/// "retains the existing owner and Communication Timeline history". Interactions recorded before
/// the conversion carry the LEAD id; those recorded after carry the DONOR id. Reading only one
/// would drop half the history at the moment the record matters most, so the handler resolves
/// whichever id it was given to BOTH and merges.
/// </summary>
public sealed class CommunicationTimelineQueryHandler(
    ILeadRepository leadRepository,
    IDonorRepository donorRepository,
    IInteractionTimelineReader timelineReader,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private const int MaximumEntries = 200;

    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<CommunicationTimelineResponse>> HandleAsync(
        GetCommunicationTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LeadId is null && query.DonorId is null)
        {
            return Result.Failure<CommunicationTimelineResponse>(Error.Validation(
                "Name the lead or the donor whose timeline you want.",
                [new ValidationError("leadId", "Give a lead id or a donor id.")]));
        }

        Lead? lead = null;
        Donor? donor = null;

        if (query.LeadId is Guid leadId)
        {
            lead = await leadRepository.GetByIdAsync(leadId, cancellationToken);

            if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
            {
                return NotFound();
            }

            // A CONVERTED LEAD CARRIES ITS DONOR. Following it is what keeps the post-conversion
            // half of the history on screen.
            if (lead.ConvertedDonorId is Guid convertedDonorId)
            {
                donor = await donorRepository.GetByIdAsync(convertedDonorId, cancellationToken);
            }
        }

        if (query.DonorId is Guid donorId)
        {
            donor = await donorRepository.GetByIdAsync(donorId, cancellationToken);

            if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
            {
                return NotFound();
            }

            // And the other direction: a donor that came from a lead keeps the lead's earlier
            // conversations, which is the half the document explicitly promises to preserve.
            lead ??= await leadRepository.GetConvertedFromAsync(donor.Id, cancellationToken);
        }

        if (lead is null && donor is null)
        {
            return NotFound();
        }

        // OWN-RECORDS SCOPE IS CHECKED ON THE LEAD, because ownership lives there. A fundraiser
        // limited to their own records must not read somebody else's donor conversations by
        // arriving with a donor id instead of a lead id.
        if (currentUser.Scope.IsOwnRecordsOnly
            && lead is not null
            && lead.OwnerUserId != currentUser.UserId)
        {
            return NotFound();
        }

        var interactions = await timelineReader.GetTimelineAsync(
            lead?.Id, donor?.Id, MaximumEntries, cancellationToken);

        var canSeeContact = currentUser.CanSeeContact();
        var now = clock.UtcNow;

        var entries = interactions
            .Select(interaction => BuildEntry(interaction, canSeeContact))
            .ToList();

        return Result.Success(new CommunicationTimelineResponse(
            ScreenIds.CommunicationTimeline,
            ScreenRoutes.CommunicationTimeline,
            lead?.Id,
            lead?.LeadReference,
            donor?.Id,
            donor?.DonorNumber,
            donor?.DisplayName ?? BuildLeadName(lead!),
            ContactMasking.Phone(lead?.MobileNumber ?? donor?.PrimaryPhone, canSeeContact),
            ContactMasking.Email(lead?.EmailAddress ?? donor?.PrimaryEmail, canSeeContact),
            lead?.Campaign?.Name,
            lead?.Source,
            lead?.PreferredLanguage ?? donor?.PreferredLanguage ?? SupportedLanguages.Default,
            lead?.OwnerName ?? donor?.RelationshipOwnerName,
            lead?.Status.ToString() ?? donor?.Status.ToString() ?? string.Empty,
            (lead?.Temperature ?? LeadTemperature.Warm).ToString(),
            (lead?.DonationPotential ?? DonationPotential.Medium).ToString(),
            lead is null ? 0 : LeadHealth.Calculate(lead, now),
            entries,
            ToLookup<LeadTemperature>(),
            ToLookup<DonationPotential>(),
            ToLookup<InteractionType>(),
            ToLookup<ContactOutcome>(),
            BuildPermittedActions(),
            !canSeeContact,
            DescribeScope(),
            entries.Count == 0 ? ScreenState.Empty : ScreenState.Initial));
    }

    /// <summary>
    /// Incoming or outgoing, decided from the outcome.
    ///
    /// THE ENTITY STORES NO DIRECTION, and the interaction TYPE cannot supply one either - a call
    /// or an e-mail goes both ways. The outcome can: a donor who called back or replied started
    /// that exchange, and everything else was something the charity did. A note is neither, and
    /// says so rather than being labelled with a guess.
    /// </summary>
    private static string DescribeDirection(DonorInteraction interaction) =>
        interaction.InteractionType == InteractionType.Note
            ? "Internal"
            : interaction.Outcome == ContactOutcome.CallbackRequested
                ? "Incoming"
                : "Outgoing";

    private static CommunicationTimelineEntryResponse BuildEntry(
        DonorInteraction interaction, bool canSeeContact) =>
        new(interaction.Id,
            interaction.InteractionType.ToString(),
            interaction.Channel?.ToString(),
            DescribeDirection(interaction),
            interaction.OccurredAtUtc,
            interaction.Outcome.ToString(),
            interaction.Name,

            // THE NOTE IS THE SENSITIVE PART. A call note routinely records what a donor said
            // about their circumstances - more revealing than the phone number beside it.
            canSeeContact ? interaction.Description : null,

            interaction.PerformedByName,
            !canSeeContact);

    private static string BuildLeadName(Lead lead) =>
        string.IsNullOrWhiteSpace(lead.LastName)
            ? lead.FirstName
            : $"{lead.FirstName} {lead.LastName}";

    private static Result<CommunicationTimelineResponse> NotFound() =>
        Result.Failure<CommunicationTimelineResponse>(
            Error.NotFound("That record was not found inside your scope."));

    /// <summary>
    /// What the caller may do on this screen.
    ///
    /// VERB LABELS, NOT PERMISSION CODES, because that is the vocabulary every other screen in
    /// this module already answers in - the lead work queue returns "Contact" and "Assign", the
    /// assignment board "Reassign". Returning raw codes here would have made this the only screen
    /// whose buttons the browser had to match differently.
    /// </summary>
    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueView))
        {
            actions.Add("View");
        }

        // Recording a conversation is the same permission as contacting a lead from the queue:
        // both write a DonorInteraction, and this screen is only a different door to it.
        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueContact))
        {
            actions.Add("Contact");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueQualify))
        {
            actions.Add("Qualify");
        }

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerSchedule))
        {
            actions.Add("Schedule follow-up");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Your own records" : "Your organisation";

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
