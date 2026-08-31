using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.Donor360.DTOs;
using YDots.DON.Application.Features.Donors.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.Donor360.Queries.GetDonor360;

/// <summary>SCR-DON-003 GET. Unified identity, donations, communications, consent, tasks and history.</summary>
public sealed record GetDonor360Query(Guid DonorId);

/// <summary>
/// Assembles the thirteen read-only panels of Donor 360.
///
/// Every panel is loaded through the repository that owns it, and each one applies its own
/// visibility rule: contact details need don.donors.view-sensitive-contact, documents and
/// evidence need don.donors.view-confidential-evidence. UI section 4.3.1: "Every tab and
/// sensitive field has separate permission and scope enforcement."
/// </summary>
public sealed class Donor360QueryHandler(
    IDonorRepository donorRepository,
    IDonor360Repository donor360Repository,
    IConsentRepository consentRepository,
    IFollowUpRepository followUpRepository,
    IDonorMergeCaseRepository mergeCaseRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    private const int HistoryRowLimit = 50;

    public async Task<Result<Donor360Response>> HandleAsync(
        GetDonor360Query query,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetWithChildrenAsync(query.DonorId, cancellationToken);

        if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<Donor360Response>(Error.DonorNotFound());
        }

        if (currentUser.Scope.IsOwnRecordsOnly && donor.RelationshipOwnerUserId != currentUser.UserId)
        {
            return Result.Failure<Donor360Response>(Error.DonorNotFound());
        }

        var canSeeContact = currentUser.CanSeeContact();
        var canSeeEvidence = currentUser.CanSeeEvidence();
        var now = clock.UtcNow;

        var contacts = await donorRepository.GetContactsAsync(donor.Id, cancellationToken);
        var tags = await donorRepository.GetTagsAsync(donor.Id, cancellationToken);
        var interactions = await donorRepository.GetInteractionsAsync(donor.Id, HistoryRowLimit, cancellationToken);
        var activity = await donorRepository.GetActivityHistoryAsync(donor.Id, HistoryRowLimit, cancellationToken);
        var consents = await consentRepository.GetCurrentForDonorAsync(donor.Id, cancellationToken);
        var followUps = await followUpRepository.GetOpenForDonorAsync(donor.Id, cancellationToken);
        var mergeCases = await mergeCaseRepository.GetForDonorAsync(donor.Id, cancellationToken);
        var totals = await donor360Repository.GetDonationSummariesAsync(donor.Id, cancellationToken);
        var promises = await donor360Repository.GetPromisesAsync(donor.Id, cancellationToken);
        var documents = await donor360Repository.GetDocumentsAsync(donor.Id, canSeeEvidence, cancellationToken);
        var campaignHistory = await donor360Repository.GetCampaignHistoryAsync(donor.Id, cancellationToken);

        var response = new Donor360Response(
            ScreenIds.Donor360,
            ScreenRoutes.Donor360,
            donor.DonorNumber,
            donor.ToDetailResponse(canSeeContact, DonorMappingConfig.PermittedActionsFor(donor)),
            BuildIdentitySummary(donor, contacts, tags, canSeeContact),
            donor.RelationshipOwnerUserId is null
                ? null
                : new RelationshipOwnerResponse(donor.RelationshipOwnerUserId.Value, donor.RelationshipOwnerName),
            BuildConsentStatus(consents),
            [.. consents.Select(BuildPreference)],
            [.. totals.Select(total => BuildDonationTotal(total, now))],
            [.. campaignHistory.Select(entry => new CampaignHistoryResponse(
                entry.Campaign.Id, entry.Campaign.Code, entry.Campaign.Name, entry.LeadReference, entry.ConvertedAtUtc))],
            [.. interactions.Select(BuildConversation)],
            [.. followUps.Select(task => new Donor360FollowUpResponse(
                task.Id, task.FollowUpReference, task.NextAction, task.DueAtUtc,
                task.Priority.ToString(), task.Status.ToString(), task.RelationshipOwnerName))],
            [.. promises.Select(promise => new PromiseResponse(
                promise.Id, promise.Reference, promise.Amount, promise.Currency,
                promise.PromisedAtUtc, promise.DueAtUtc, promise.Status.ToString(), promise.Campaign?.Name))],
            [.. documents.Select(document => new DocumentResponse(
                document.Id, document.Reference, document.Name, document.Description,
                document.Classification.ToString(), document.ScanStatus, document.CreatedAtUtc, document.ExpiresAtUtc))],
            [.. mergeCases.Select(mergeCase => new DuplicateLinkResponse(
                mergeCase.Id, mergeCase.ReviewReference, mergeCase.Status.ToString(),
                mergeCase.IdentityConfidence.ToString(), mergeCase.Decision?.ToString(),
                $"{ScreenRoutes.DuplicateReview}?reviewId={mergeCase.Id}"))],
            [.. activity.Select(entry => new ActivityHistoryResponse(
                entry.Id, entry.ActionCode, entry.TargetType, entry.Result.ToString(),
                entry.Reason, entry.CreatedAtUtc, entry.CorrelationId))],
            BuildPermittedActions(donor),
            BuildMaskedFieldList(canSeeContact, canSeeEvidence),
            DescribeScope(),
            ScreenState.Initial);

        // Opening a 360 view with the unmasking permission is a sensitive view in its own right.
        if (canSeeContact || canSeeEvidence)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.DonorSensitiveViewed, nameof(Donor), donor.Id, AuditResult.Succeeded,
                    $"{donor.DonorNumber} opened on Donor 360 with elevated visibility."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }

    private static IdentityAndContactSummaryResponse BuildIdentitySummary(
        Donor donor,
        IReadOnlyList<DonorContact> contacts,
        IReadOnlyList<DonorTag> tags,
        bool canSeeContact) =>
        new(
            donor.DisplayName,
            donor.DonorType.ToString(),
            ContactMasking.Email(donor.PrimaryEmail, canSeeContact),
            ContactMasking.Phone(donor.PrimaryPhone, canSeeContact),
            donor.PreferredLanguage,
            donor.DoNotContact,
            [.. contacts.Select(contact => new DonorContactResponse(
                contact.Id,
                contact.Name,
                contact.Description,
                contact.Channel.ToString(),
                MaskContactValue(contact, canSeeContact),
                contact.IsPrimary,
                contact.IsVerified,
                contact.Status.ToString(),
                !canSeeContact))],
            [.. tags.Select(tag => new DonorTagResponse(tag.Id, tag.Code, tag.Name, tag.Description, tag.Status.ToString()))],
            !canSeeContact);

    private static string MaskContactValue(DonorContact contact, bool canSeeContact) =>
        contact.Channel switch
        {
            ContactChannel.Email => ContactMasking.Email(contact.Value, canSeeContact) ?? string.Empty,
            ContactChannel.PostalAddress => canSeeContact ? contact.Value : "Address hidden",
            _ => ContactMasking.Phone(contact.Value, canSeeContact) ?? string.Empty
        };

    /// <summary>
    /// The overall consent badge. "Withdrawn" wins over "Granted" whenever any channel has been
    /// refused, because the safe reading of a mixed picture is the restrictive one.
    /// </summary>
    private static ConsentStatusResponse BuildConsentStatus(IReadOnlyList<Consent> consents)
    {
        var granted = consents.Count(consent => consent.ConsentState == ConsentState.Granted);
        var withdrawn = consents.Count(consent => consent.ConsentState == ConsentState.Withdrawn);

        var overall = consents.Count == 0
            ? "Not recorded"
            : withdrawn > 0 && granted == 0 ? "Withdrawn"
            : withdrawn > 0 ? "Partial"
            : "Granted";

        return new ConsentStatusResponse(
            overall,
            granted,
            withdrawn,
            consents.Count == 0 ? null : consents.Max(consent => consent.EffectiveAtUtc),
            consents.OrderByDescending(consent => consent.EffectiveAtUtc).FirstOrDefault()?.NoticeVersion);
    }

    private static CommunicationPreferenceResponse BuildPreference(Consent consent) =>
        new(
            consent.Channel.ToString(),
            consent.ConsentState.ToString(),
            consent.Status.ToString(),
            consent.EffectiveAtUtc,
            consent.ExpiryAtUtc,
            consent.PublicRecognitionPreference);

    /// <summary>
    /// Source freshness in words. A number that is a week old and a number from this morning
    /// look identical on screen otherwise, and the field contract asks for the difference.
    /// </summary>
    private static DonationTotalResponse BuildDonationTotal(DonorDonationSummary summary, DateTimeOffset now)
    {
        var age = now - summary.RefreshedAtUtc;

        var freshness = age.TotalHours switch
        {
            < 1 => "Up to date",
            < 24 => $"Refreshed {(int)age.TotalHours} hour(s) ago",
            < 168 => $"Refreshed {(int)age.TotalDays} day(s) ago",
            _ => "Stale - refresh pending"
        };

        return new DonationTotalResponse(
            summary.Stage.ToString(),
            summary.Currency,
            summary.TotalAmount,
            summary.TransactionCount,
            summary.AsAtUtc,
            summary.RefreshedAtUtc,
            freshness);
    }

    private static ConversationResponse BuildConversation(DonorInteraction interaction) =>
        new(
            interaction.Id,
            interaction.Name,
            interaction.Description,
            interaction.InteractionType.ToString(),
            interaction.Channel?.ToString(),
            interaction.OccurredAtUtc,
            interaction.Outcome.ToString(),
            interaction.PerformedByName,
            interaction.Status.ToString());

    private IReadOnlyList<string> BuildPermittedActions(Donor donor)
    {
        var actions = new List<string> { "View" };

        if (currentUser.HasPermission(PermissionCodes.Donor360Correct)
            && donor.Status is not (DonorStatus.Archived or DonorStatus.Merged))
        {
            actions.Add("Correct");
        }

        if (currentUser.HasPermission(PermissionCodes.Donor360FollowUp))
        {
            actions.Add("Follow up");
        }

        if (currentUser.HasPermission(PermissionCodes.Donor360CreateIntent))
        {
            actions.Add("Create intent");
        }

        if (currentUser.HasPermission(PermissionCodes.Donor360DeleteDraft)
            && donor.Status == DonorStatus.Prospect
            && donor.ApprovalState == ApprovalState.NotSubmitted)
        {
            actions.Add("Delete unused draft");
        }

        return actions;
    }

    /// <summary>
    /// Tells the UI which panels are showing masked values, so it can explain the state instead
    /// of quietly showing stars and leaving the person wondering.
    /// </summary>
    private static IReadOnlyList<string> BuildMaskedFieldList(bool canSeeContact, bool canSeeEvidence)
    {
        var masked = new List<string>();

        if (!canSeeContact)
        {
            masked.Add("Identity and contact summary");
        }

        if (!canSeeEvidence)
        {
            masked.Add("Documents");
        }

        return masked;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";
}
