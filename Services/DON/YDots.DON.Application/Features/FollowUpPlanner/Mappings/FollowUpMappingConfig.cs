using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.FollowUpPlanner.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.FollowUpPlanner.Mappings;

/// <summary>Manual mapping for DON-UI-08.</summary>
public static class FollowUpMappingConfig
{
    public static FollowUpResponse ToResponse(
        this FollowUpTask task,
        bool canSeeContact,
        bool canSeeEvidence,
        ConsentWarningResponse consentWarning) =>
        new(
            task.Id,
            task.FollowUpReference,
            task.DonorId,
            task.Donor?.DonorNumber,
            task.Donor?.DisplayName,
            task.LeadId,
            task.Lead?.LeadReference,
            task.RelationshipOwnerUserId,
            task.RelationshipOwnerName,
            task.Purpose,
            task.PermittedChannel.ToString(),
            task.PreferredLanguage,
            canSeeContact ? task.PreferredContactTimeUtc : null,
            task.NextAction,
            task.DueAtUtc,
            task.Priority.ToString(),
            ContactMasking.Confidential(task.Notes, canSeeEvidence),
            task.ConsentWarningAcknowledged,
            task.ConsentNoticeVersion,
            task.ConsentAcknowledgedAtUtc,
            task.Status.ToString(),
            task.CompletedAtUtc,
            task.CompletionOutcome,
            task.RescheduleReason,
            task.CancellationReason,
            task.CreatedAtUtc,
            task.Version,
            !canSeeEvidence,
            !canSeeContact,
            consentWarning,
            PermittedActionsFor(task));

    /// <summary>Which actions the task state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(FollowUpTask task) =>
        task.Status switch
        {
            FollowUpStatus.Planned or FollowUpStatus.Assigned or FollowUpStatus.Rescheduled =>
                ["Assign", "Mark complete", "Reschedule", "Cancel task"],
            _ => ["View"]
        };

    /// <summary>
    /// Turns the donor's consent rows into the warning the planner has to display.
    ///
    /// The warning is built from what consent actually says rather than from a stored flag, so
    /// a withdrawal recorded five minutes ago is reflected the next time the screen is opened.
    /// A "do not contact" donor produces a Blocking warning: no channel is permitted at all.
    /// </summary>
    public static ConsentWarningResponse BuildConsentWarning(
        Donor? donor,
        IReadOnlyList<Consent> consents)
    {
        var everyChannel = Enum.GetValues<ConsentChannel>();

        if (donor is not null && donor.DoNotContact)
        {
            return new ConsentWarningResponse(
                true,
                "Blocking",
                "This donor is marked Do not contact. No follow-up may be scheduled on any channel.",
                [],
                [.. everyChannel.Select(channel => channel.ToString())]);
        }

        var granted = consents
            .Where(consent => consent.Status == ConsentStatus.Active && consent.ConsentState == ConsentState.Granted)
            .Select(consent => consent.Channel)
            .Distinct()
            .ToList();

        var refused = consents
            .Where(consent => consent.ConsentState == ConsentState.Withdrawn)
            .Select(consent => consent.Channel)
            .Distinct()
            .Where(channel => !granted.Contains(channel))
            .ToList();

        // Nothing recorded at all is its own case: it is not a refusal, but it is not a
        // permission either, and the planner has to say so rather than assume.
        if (consents.Count == 0)
        {
            return new ConsentWarningResponse(
                true,
                "Caution",
                "No consent has been recorded for this record. Confirm permission before scheduling contact.",
                [],
                []);
        }

        var prohibited = refused.Select(channel => channel.ToString()).ToList();

        return granted.Count == 0
            ? new ConsentWarningResponse(
                true,
                "Blocking",
                "Every recorded channel has been withdrawn. No follow-up may be scheduled.",
                [],
                prohibited)
            : new ConsentWarningResponse(
                prohibited.Count > 0,
                prohibited.Count > 0 ? "Caution" : "None",
                prohibited.Count > 0
                    ? $"Contact is not permitted by {string.Join(", ", prohibited)}. Use a permitted channel."
                    : "Every recorded channel permits contact.",
                [.. granted.Select(channel => channel.ToString())],
                prohibited);
    }
}
