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
using YDots.DON.Application.Features.Donors.Mappings;
using YDots.DON.Application.Features.LeadWorkQueue.DTOs;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.Events;

namespace YDots.DON.Application.Features.LeadWorkQueue.Commands.LeadWorkQueueActions;

/// <summary>SCR-DON-001 Accept. The caller takes ownership of the lead.</summary>
public sealed record AcceptLeadCommand(Guid LeadId, AcceptLeadRequest Request);

/// <summary>SCR-DON-001 Assign. Hands the lead to somebody else with a recorded reason.</summary>
public sealed record AssignLeadCommand(Guid LeadId, AssignLeadRequest Request);

/// <summary>SCR-DON-001 Contact. Records a conversation and its outcome.</summary>
public sealed record ContactLeadCommand(Guid LeadId, ContactLeadRequest Request);

/// <summary>SCR-DON-001 Qualify. Moves the lead to Qualified or parks it in Nurture.</summary>
public sealed record QualifyLeadCommand(Guid LeadId, QualifyLeadRequest Request);

/// <summary>SCR-DON-001 Close. Danger action: named reason, history preserved.</summary>
public sealed record CloseLeadCommand(Guid LeadId, ReasonRequest Request);

/// <summary>Step 5 of the guided flow: create or link the donor and preserve attribution.</summary>
public sealed record ConvertLeadCommand(Guid LeadId, ConvertLeadRequest Request);

/// <summary>
/// The five queue actions plus conversion.
///
/// Each one is a named transition, never a free-form status edit. UI section 5.5: "Move only
/// through a named action with reason/evidence when required", so the allowed source states
/// are spelled out at the top of every method.
/// </summary>
public sealed class LeadWorkQueueCommandHandler(
    ILeadRepository leadRepository,
    IDonorRepository donorRepository,
    IConsentRepository consentRepository,
    IReferenceNumberGenerator referenceNumbers,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        AcceptLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status is not (LeadStatus.New or LeadStatus.Nurture))
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"Only a New or Nurture lead can be accepted. This lead is {lead.Status}."));
        }

        var now = clock.UtcNow;
        var previousOwnerId = lead.OwnerUserId;
        var previousOwnerName = lead.OwnerName;

        lead.OwnerUserId = currentUser.UserId;
        lead.OwnerName = currentUser.DisplayName;
        lead.Status = LeadStatus.Assigned;
        lead.AcceptedAtUtc = now;
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, now, _settings);

        leadRepository.AddAssignment(new LeadAssignment
        {
            OrganisationId = lead.OrganisationId,
            LeadId = lead.Id,
            PreviousOwnerUserId = previousOwnerId,
            PreviousOwnerName = previousOwnerName,
            NewOwnerUserId = currentUser.UserId,
            NewOwnerName = currentUser.DisplayName ?? currentUser.UserId.ToString(),
            AssignmentReason = command.Request.Comment?.Trim() ?? "Accepted from the lead work queue.",
            EffectiveAtUtc = now,
            AssignedByUserId = currentUser.UserId
        });

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadAccepted, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{lead.LeadReference} accepted by {currentUser.DisplayName}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        AssignLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status is LeadStatus.Converted or LeadStatus.Closed or LeadStatus.Suppressed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"A lead in state {lead.Status} can no longer be assigned."));
        }

        var now = command.Request.EffectiveAtUtc ?? clock.UtcNow;
        var previousOwnerId = lead.OwnerUserId;
        var previousOwnerName = lead.OwnerName;

        lead.OwnerUserId = command.Request.NewOwnerUserId;
        lead.OwnerName = command.Request.NewOwnerName.Trim();
        lead.TeamCode = command.Request.TeamCode?.Trim() ?? lead.TeamCode;

        if (lead.Status == LeadStatus.New)
        {
            lead.Status = LeadStatus.Assigned;
        }

        leadRepository.AddAssignment(new LeadAssignment
        {
            OrganisationId = lead.OrganisationId,
            LeadId = lead.Id,
            PreviousOwnerUserId = previousOwnerId,
            PreviousOwnerName = previousOwnerName,
            NewOwnerUserId = command.Request.NewOwnerUserId,
            NewOwnerName = command.Request.NewOwnerName.Trim(),
            AssignmentReason = command.Request.AssignmentReason.Trim(),
            EffectiveAtUtc = now,
            AssignedByUserId = currentUser.UserId
        });

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadAssigned, nameof(Lead), lead.Id, AuditResult.Succeeded,
                command.Request.AssignmentReason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        ContactLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status is LeadStatus.Converted or LeadStatus.Closed or LeadStatus.Suppressed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"A lead in state {lead.Status} can no longer be contacted."));
        }

        if (!Enum.TryParse<ConsentChannel>(command.Request.Channel, ignoreCase: true, out var channel))
        {
            return Result.Failure<LeadDetailResponse>(Error.Validation(
                "Review Channel. Choose a value from the approved catalogue.",
                [new ValidationError(nameof(command.Request.Channel), "Choose a channel from the list.")]));
        }

        if (!Enum.TryParse<ContactOutcome>(command.Request.Outcome, ignoreCase: true, out var outcome))
        {
            return Result.Failure<LeadDetailResponse>(Error.Validation(
                "Review Outcome. Choose a value from the approved catalogue.",
                [new ValidationError(nameof(command.Request.Outcome), "Choose an outcome from the list.")]));
        }

        // Consent gate. Contacting somebody on a channel they refused is the one mistake this
        // whole section exists to prevent, so it is blocked here rather than warned about.
        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
        var permission = consents.FirstOrDefault(consent => consent.Channel == channel);

        if (permission is not null && permission.ConsentState == ConsentState.Withdrawn)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"This person has not permitted contact by {channel}. Choose a permitted channel."));
        }

        var occurred = command.Request.OccurredAtUtc ?? clock.UtcNow;

        donorRepository.AddInteraction(new DonorInteraction
        {
            LeadId = lead.Id,
            OrganisationId = lead.OrganisationId,
            Name = $"{channel} contact - {lead.LeadReference}",
            Description = command.Request.Notes?.Trim(),
            Status = DonorInteractionStatus.Completed,
            InteractionType = MapChannelToInteraction(channel),
            Channel = channel,
            OccurredAtUtc = occurred,
            Outcome = outcome,
            PerformedByUserId = currentUser.UserId,
            PerformedByName = currentUser.DisplayName
        });

        lead.LastContactOutcome = outcome;
        lead.LastContactedAtUtc = occurred;
        lead.NextAction = command.Request.NextAction?.Trim() ?? lead.NextAction;
        lead.NextActionDueUtc = command.Request.NextActionDueUtc ?? lead.NextActionDueUtc;
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        if (lead.Status is LeadStatus.New or LeadStatus.Assigned)
        {
            lead.Status = LeadStatus.Contacted;
        }

        // A refusal is a decision, not a failed call. Suppressing the lead stops anybody in
        // the team ringing them again next week.
        if (outcome is ContactOutcome.DoNotContact)
        {
            lead.Status = LeadStatus.Suppressed;
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadContacted, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{lead.LeadReference} contacted by {channel}. Outcome {outcome}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        QualifyLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status is not (LeadStatus.Assigned or LeadStatus.Contacted or LeadStatus.Nurture))
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"Only an assigned, contacted or nurture lead can be qualified. This lead is {lead.Status}."));
        }

        if (lead.OwnerUserId is null)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "Assign an owner before qualifying. Step 3 of the flow requires explicit ownership."));
        }

        var now = clock.UtcNow;

        lead.Status = command.Request.MoveToNurture ? LeadStatus.Nurture : LeadStatus.Qualified;
        lead.QualifiedAtUtc = command.Request.MoveToNurture ? null : now;
        lead.NextAction = command.Request.NextAction?.Trim() ?? lead.NextAction;
        lead.NextActionDueUtc = command.Request.NextActionDueUtc ?? lead.NextActionDueUtc;
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, now, _settings);

        donorRepository.AddInteraction(new DonorInteraction
        {
            LeadId = lead.Id,
            OrganisationId = lead.OrganisationId,
            Name = $"Qualification - {lead.LeadReference}",
            Description = command.Request.QualificationNotes.Trim(),
            Status = DonorInteractionStatus.Completed,
            InteractionType = InteractionType.Note,
            OccurredAtUtc = now,
            Outcome = ContactOutcome.Reached,
            PerformedByUserId = currentUser.UserId,
            PerformedByName = currentUser.DisplayName
        });

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadQualified, nameof(Lead), lead.Id, AuditResult.Succeeded,
                command.Request.QualificationNotes.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        CloseLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status is LeadStatus.Converted or LeadStatus.Closed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"A lead in state {lead.Status} cannot be closed again."));
        }

        lead.Status = LeadStatus.Closed;
        lead.ClosureReason = command.Request.Reason.Trim();
        lead.NextAction = null;
        lead.NextActionDueUtc = null;
        lead.SlaState = SlaState.NotApplicable;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadClosed, nameof(Lead), lead.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        ConvertLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(command.LeadId, command.Request.ExpectedVersion, cancellationToken);
        if (loaded.Error is not null)
        {
            return Result.Failure<LeadDetailResponse>(loaded.Error);
        }

        var lead = loaded.Lead!;

        if (lead.Status != LeadStatus.Qualified)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"Only a qualified lead can be converted. This lead is {lead.Status}."));
        }

        if (lead.ConvertedDonorId is not null)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "This lead has already been converted."));
        }

        var now = clock.UtcNow;
        Donor donor;

        if (command.Request.ExistingDonorId is not null)
        {
            var existing = await donorRepository.GetByIdAsync(command.Request.ExistingDonorId.Value, cancellationToken);

            if (existing is null || existing.OrganisationId != currentUser.OrganisationId)
            {
                return Result.Failure<LeadDetailResponse>(Error.DonorNotFound());
            }

            donor = existing;
            donor.SourceLeadId ??= lead.Id;
        }
        else
        {
            var donorType = Enum.TryParse<DonorType>(command.Request.DonorType, ignoreCase: true, out var parsed)
                ? parsed
                : DonorType.Individual;

            var businessKey = DonorMappingConfig.BuildBusinessKey(
                lead.EmailAddress, lead.MobileNumber, donorType, lead.FirstName, lead.LastName, null);

            if (await donorRepository.ExistsByBusinessKeyAsync(businessKey, null, cancellationToken))
            {
                return Result.Failure<LeadDetailResponse>(Error.Duplicate(
                    "A donor with the same contact detail already exists. Link to it instead of creating a second record."));
            }

            donor = new Donor
            {
                OrganisationId = lead.OrganisationId,
                DonorNumber = await referenceNumbers.NextDonorNumberAsync(cancellationToken),
                DonorType = donorType,
                FirstName = lead.FirstName,
                LastName = lead.LastName,
                PrimaryEmail = lead.EmailAddress,
                PrimaryPhone = lead.MobileNumber,
                PreferredLanguage = lead.PreferredLanguage,
                Status = DonorStatus.Prospect,
                ApprovalState = ApprovalState.NotSubmitted,
                RelationshipOwnerUserId = lead.OwnerUserId,
                RelationshipOwnerName = lead.OwnerName,
                SourceLeadId = lead.Id,
                NormalizedBusinessKey = businessKey
            };

            await donorRepository.AddAsync(donor, cancellationToken);

            outboxWriter.Write(
                IntegrationEventNames.DonorCreatedV1, nameof(Donor), donor.Id,
                new DonorCreatedV1(donor.Id, donor.DonorNumber, donor.DonorType.ToString(), donor.OrganisationId, now));
        }

        // Attribution is preserved by moving the consent rows across rather than copying them.
        // The evidence stays the same row, so its history and its notice version survive.
        var leadConsents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
        foreach (var consent in leadConsents)
        {
            consent.DonorId = donor.Id;
        }

        lead.Status = LeadStatus.Converted;
        lead.ConvertedDonorId = donor.Id;
        lead.ConvertedAtUtc = now;
        lead.SlaState = SlaState.NotApplicable;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadConverted, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{lead.LeadReference} converted to donor {donor.DonorNumber}. {command.Request.ConversionReason.Trim()}"),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(lead, cancellationToken);
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

    private async Task<Result<LeadDetailResponse>> BuildDetailAsync(Lead lead, CancellationToken cancellationToken)
    {
        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);

        return Result.Success(lead.ToDetailResponse(
            currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    /// <summary>Load, scope check and optional version check in one place, since all six actions need it.</summary>
    private async Task<(Lead? Lead, Error? Error)> LoadAsync(
        Guid leadId,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var lead = await leadRepository.GetByIdAsync(leadId, cancellationToken);

        if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
        {
            return (null, Error.NotFound("That lead was not found inside your scope."));
        }

        if (currentUser.Scope.IsOwnRecordsOnly && lead.OwnerUserId != currentUser.UserId)
        {
            return (null, Error.NotFound("That lead was not found inside your scope."));
        }

        if (expectedVersion is > 0 && expectedVersion != lead.Version)
        {
            return (null, Error.Concurrency());
        }

        return (lead, null);
    }
}
