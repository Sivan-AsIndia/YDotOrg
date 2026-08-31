using FluentValidation;
using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.FollowUpPlanner.Commands.PlanFollowUp;

/// <summary>DON-UI-08 Schedule follow-up. The primary action.</summary>
public sealed record ScheduleFollowUpCommand(ScheduleFollowUpRequest Request);

/// <summary>DON-UI-08 Assign.</summary>
public sealed record AssignFollowUpCommand(Guid FollowUpId, AssignFollowUpRequest Request);

/// <summary>DON-UI-08 Mark complete.</summary>
public sealed record CompleteFollowUpCommand(Guid FollowUpId, CompleteFollowUpRequest Request);

/// <summary>DON-UI-08 Reschedule.</summary>
public sealed record RescheduleFollowUpCommand(Guid FollowUpId, RescheduleFollowUpRequest Request);

/// <summary>DON-UI-08 Cancel task. Danger action: named reason required.</summary>
public sealed record CancelFollowUpCommand(Guid FollowUpId, ReasonRequest Request);

/// <summary>
/// The follow-up planner write side.
///
/// "Plan a respectful, consent-aware next action" is the purpose of this screen, and the word
/// that matters is consent-aware: scheduling is refused outright when the chosen channel is not
/// permitted, rather than warned about and allowed through.
/// </summary>
public sealed class FollowUpCommandHandler(
    IFollowUpRepository followUpRepository,
    IConsentRepository consentRepository,
    IDonorRepository donorRepository,
    ILeadRepository leadRepository,
    IReferenceNumberGenerator referenceNumbers,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<FollowUpResponse>> HandleAsync(
        ScheduleFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        if (request.DonorId is null && request.LeadId is null)
        {
            return Result.Failure<FollowUpResponse>(Error.Validation(
                "Enter Donor or lead reference.",
                [new ValidationError(nameof(request.DonorId), "Choose a donor or a lead.")]));
        }

        if (!Enum.TryParse<ConsentChannel>(request.PermittedChannel, ignoreCase: true, out var channel))
        {
            return Result.Failure<FollowUpResponse>(Error.Validation(
                "Review Permitted channel. Choose a value from the approved catalogue.",
                [new ValidationError(nameof(request.PermittedChannel), "Choose a channel from the list.")]));
        }

        Donor? donor = null;
        Lead? lead = null;

        if (request.DonorId is not null)
        {
            donor = await donorRepository.GetByIdAsync(request.DonorId.Value, cancellationToken);

            if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
            {
                return Result.Failure<FollowUpResponse>(Error.DonorNotFound());
            }
        }

        if (request.LeadId is not null)
        {
            lead = await leadRepository.GetByIdAsync(request.LeadId.Value, cancellationToken);

            if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
            {
                return Result.Failure<FollowUpResponse>(Error.NotFound("That lead was not found inside your scope."));
            }
        }

        var consents = donor is not null
            ? await consentRepository.GetCurrentForDonorAsync(donor.Id, cancellationToken)
            : await consentRepository.GetForLeadAsync(lead!.Id, cancellationToken);

        var warning = FollowUpMappingConfig.BuildConsentWarning(donor, consents);

        if (string.Equals(warning.Level, "Blocking", StringComparison.Ordinal))
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(warning.Message));
        }

        if (warning.ProhibitedChannels.Contains(channel.ToString(), StringComparer.Ordinal))
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(
                $"Contact by {channel} has been withdrawn for this record. Choose a permitted channel."));
        }

        // The acknowledgement is only demanded when there is actually something to acknowledge,
        // and it is never pre-selected for the caller.
        if (warning.HasWarning && !request.ConsentWarningAcknowledged)
        {
            return Result.Failure<FollowUpResponse>(Error.Validation(
                warning.Message + " Acknowledge the consent warning before scheduling.",
                [new ValidationError(nameof(request.ConsentWarningAcknowledged), "Read and accept the consent warning.")]));
        }

        var now = clock.UtcNow;

        if (request.DueAtUtc < now)
        {
            return Result.Failure<FollowUpResponse>(Error.Validation(
                "Review Due date and time. It cannot be in the past.",
                [new ValidationError(nameof(request.DueAtUtc), "Choose a future date and time.")]));
        }

        var priority = Enum.TryParse<FollowUpPriority>(request.Priority, ignoreCase: true, out var parsedPriority)
            ? parsedPriority
            : FollowUpPriority.Normal;

        var task = new FollowUpTask
        {
            OrganisationId = currentUser.OrganisationId,
            FollowUpReference = await referenceNumbers.NextFollowUpReferenceAsync(cancellationToken),
            DonorId = donor?.Id,
            LeadId = lead?.Id,
            RelationshipOwnerUserId = request.RelationshipOwnerUserId
                                      ?? donor?.RelationshipOwnerUserId
                                      ?? lead?.OwnerUserId
                                      ?? currentUser.UserId,
            RelationshipOwnerName = request.RelationshipOwnerName?.Trim()
                                    ?? donor?.RelationshipOwnerName
                                    ?? lead?.OwnerName
                                    ?? currentUser.DisplayName,
            Purpose = request.Purpose.Trim(),
            PermittedChannel = channel,
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? donor?.PreferredLanguage ?? lead?.PreferredLanguage ?? SupportedLanguages.Default
                : request.PreferredLanguage.Trim(),
            PreferredContactTimeUtc = request.PreferredContactTimeUtc ?? lead?.PreferredContactTimeUtc,
            NextAction = request.NextAction.Trim(),
            DueAtUtc = request.DueAtUtc,
            Priority = priority,
            Notes = request.Notes?.Trim(),
            ConsentWarningAcknowledged = request.ConsentWarningAcknowledged,
            ConsentNoticeVersion = request.ConsentWarningAcknowledged ? _settings.CurrentNoticeVersion : null,
            ConsentAcknowledgedAtUtc = request.ConsentWarningAcknowledged ? now : null,
            Status = FollowUpStatus.Planned
        };

        followUpRepository.Add(task);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.FollowUpScheduled, nameof(FollowUpTask), task.Id, AuditResult.Succeeded,
                $"{task.FollowUpReference} scheduled by {channel} for {task.DueAtUtc:u}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        task.Donor = donor;
        task.Lead = lead;

        return Result.Success(task.ToResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), warning));
    }

    public async Task<Result<FollowUpResponse>> HandleAsync(
        AssignFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.FollowUpId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<FollowUpResponse>(loaded.Error);
        }

        var task = loaded.Task!;

        if (task.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(
                $"A follow-up in state {task.Status} can no longer be assigned."));
        }

        task.RelationshipOwnerUserId = command.Request.RelationshipOwnerUserId;
        task.RelationshipOwnerName = command.Request.RelationshipOwnerName.Trim();
        task.Status = FollowUpStatus.Assigned;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.FollowUpAssigned, nameof(FollowUpTask), task.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(task, cancellationToken);
    }

    public async Task<Result<FollowUpResponse>> HandleAsync(
        CompleteFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.FollowUpId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<FollowUpResponse>(loaded.Error);
        }

        var task = loaded.Task!;

        if (task.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(
                $"A follow-up in state {task.Status} cannot be completed again."));
        }

        var completedAt = command.Request.CompletedAtUtc ?? clock.UtcNow;

        task.Status = FollowUpStatus.Completed;
        task.CompletedAtUtc = completedAt;
        task.CompletionOutcome = command.Request.CompletionOutcome.Trim();

        // Completing a follow-up is a real conversation, so it joins the interaction log the
        // Donor 360 Conversations panel reads. Otherwise the activity would exist only here.
        if (task.DonorId is not null)
        {
            donorRepository.AddInteraction(new DonorInteraction
            {
                DonorId = task.DonorId,
                LeadId = task.LeadId,
                OrganisationId = task.OrganisationId,
                Name = $"Follow-up completed - {task.FollowUpReference}",
                Description = command.Request.CompletionOutcome.Trim(),
                Status = DonorInteractionStatus.Completed,
                InteractionType = MapChannelToInteraction(task.PermittedChannel),
                Channel = task.PermittedChannel,
                OccurredAtUtc = completedAt,
                Outcome = ContactOutcome.Reached,
                PerformedByUserId = currentUser.UserId,
                PerformedByName = currentUser.DisplayName
            });
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.FollowUpCompleted, nameof(FollowUpTask), task.Id, AuditResult.Succeeded,
                command.Request.CompletionOutcome.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(task, cancellationToken);
    }

    public async Task<Result<FollowUpResponse>> HandleAsync(
        RescheduleFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.FollowUpId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<FollowUpResponse>(loaded.Error);
        }

        var task = loaded.Task!;

        if (task.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(
                $"A follow-up in state {task.Status} can no longer be rescheduled."));
        }

        if (command.Request.DueAtUtc < clock.UtcNow)
        {
            return Result.Failure<FollowUpResponse>(Error.Validation(
                "Review Due date and time. It cannot be in the past.",
                [new ValidationError(nameof(command.Request.DueAtUtc), "Choose a future date and time.")]));
        }

        task.DueAtUtc = command.Request.DueAtUtc;
        task.RescheduleReason = command.Request.RescheduleReason.Trim();
        task.Status = FollowUpStatus.Rescheduled;

        if (Enum.TryParse<FollowUpPriority>(command.Request.Priority, ignoreCase: true, out var priority))
        {
            task.Priority = priority;
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.FollowUpRescheduled, nameof(FollowUpTask), task.Id, AuditResult.Succeeded,
                command.Request.RescheduleReason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(task, cancellationToken);
    }

    public async Task<Result<FollowUpResponse>> HandleAsync(
        CancelFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.FollowUpId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<FollowUpResponse>(loaded.Error);
        }

        var task = loaded.Task!;

        if (task.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
        {
            return Result.Failure<FollowUpResponse>(Error.InvalidTransition(
                $"A follow-up in state {task.Status} cannot be cancelled."));
        }

        task.Status = FollowUpStatus.Cancelled;
        task.CancellationReason = command.Request.Reason.Trim();

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.FollowUpCancelled, nameof(FollowUpTask), task.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(task, cancellationToken);
    }

    private static InteractionType MapChannelToInteraction(ConsentChannel channel) =>
        channel switch
        {
            ConsentChannel.Email => InteractionType.Email,
            ConsentChannel.Sms => InteractionType.Sms,
            ConsentChannel.WhatsApp => InteractionType.WhatsApp,
            ConsentChannel.PhoneCall => InteractionType.Call,
            _ => InteractionType.Note
        };

    private async Task<Result<FollowUpResponse>> BuildResponseAsync(FollowUpTask task, CancellationToken cancellationToken)
    {
        Donor? donor = null;
        IReadOnlyList<Consent> consents = [];

        if (task.DonorId is not null)
        {
            donor = await donorRepository.GetByIdAsync(task.DonorId.Value, cancellationToken);
            consents = await consentRepository.GetCurrentForDonorAsync(task.DonorId.Value, cancellationToken);
        }
        else if (task.LeadId is not null)
        {
            consents = await consentRepository.GetForLeadAsync(task.LeadId.Value, cancellationToken);
        }

        var warning = FollowUpMappingConfig.BuildConsentWarning(donor, consents);

        return Result.Success(task.ToResponse(
            currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), warning));
    }

    private async Task<(FollowUpTask? Task, Error? Error)> LoadAsync(
        Guid followUpId,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var task = await followUpRepository.GetByIdAsync(followUpId, cancellationToken);

        if (task is null || task.OrganisationId != currentUser.OrganisationId)
        {
            return (null, Error.NotFound("That follow-up was not found inside your scope."));
        }

        if (currentUser.Scope.IsOwnRecordsOnly && task.RelationshipOwnerUserId != currentUser.UserId)
        {
            return (null, Error.NotFound("That follow-up was not found inside your scope."));
        }

        if (expectedVersion is > 0 && expectedVersion != task.Version)
        {
            return (null, Error.Concurrency());
        }

        return (task, null);
    }
}

public sealed class ScheduleFollowUpRequestValidator : AbstractValidator<ScheduleFollowUpRequest>
{
    public ScheduleFollowUpRequestValidator()
    {
        RuleFor(request => request.Purpose)
            .NotEmpty().WithMessage("Enter Purpose.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.PermittedChannel)
            .NotEmpty().WithMessage("Enter Permitted channel.");

        RuleFor(request => request.NextAction)
            .NotEmpty().WithMessage("Enter Next action.")
            .MaximumLength(300).WithMessage("Use no more than 300 characters.");

        RuleFor(request => request.DueAtUtc)
            .NotEmpty().WithMessage("Enter Due date and time.");

        RuleFor(request => request.PreferredLanguage)
            .Must(SupportedLanguages.IsSupported)
            .WithMessage("Review Preferred language. Choose a value from the approved catalogue.")
            .When(request => !string.IsNullOrWhiteSpace(request.PreferredLanguage));

        RuleFor(request => request.Notes)
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.Notes));
    }
}

public sealed class AssignFollowUpRequestValidator : AbstractValidator<AssignFollowUpRequest>
{
    public AssignFollowUpRequestValidator()
    {
        RuleFor(request => request.RelationshipOwnerUserId).NotEmpty().WithMessage("Enter Relationship owner.");

        RuleFor(request => request.RelationshipOwnerName)
            .NotEmpty().WithMessage("Enter Relationship owner.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.");

        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Enter Reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}

public sealed class CompleteFollowUpRequestValidator : AbstractValidator<CompleteFollowUpRequest>
{
    public CompleteFollowUpRequestValidator()
    {
        RuleFor(request => request.CompletionOutcome)
            .NotEmpty().WithMessage("Enter Outcome.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}

public sealed class RescheduleFollowUpRequestValidator : AbstractValidator<RescheduleFollowUpRequest>
{
    public RescheduleFollowUpRequestValidator()
    {
        RuleFor(request => request.DueAtUtc).NotEmpty().WithMessage("Enter Due date and time.");

        RuleFor(request => request.RescheduleReason)
            .NotEmpty().WithMessage("Enter Reschedule reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}
