using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Application.Features.Donors.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.Events;

namespace YDots.DON.Application.Features.Donors.Commands.ManageDonor;

// ---- The six commands from the section 7 use-case inventory -------------------------------

/// <summary>POST /api/v1/donors. Create permission plus the duplicate check.</summary>
public sealed record CreateDonorCommand(CreateDonorRequest Request);

/// <summary>PUT /api/v1/donors/{id}. Edit permission plus expectedVersion.</summary>
public sealed record UpdateDonorCommand(Guid DonorId, UpdateDonorRequest Request);

/// <summary>POST /api/v1/donors/{id}/submit. Submit permission plus a valid state.</summary>
public sealed record SubmitDonorCommand(Guid DonorId, TransitionRequest Request);

/// <summary>POST /api/v1/donors/{id}/approve. Approve permission plus maker/checker.</summary>
public sealed record ApproveDonorCommand(Guid DonorId, DecisionRequest Request);

/// <summary>POST /api/v1/donors/{id}/cancel. Cancel permission; the reason is mandatory.</summary>
public sealed record CancelDonorCommand(Guid DonorId, ReasonRequest Request);

/// <summary>POST /api/v1/donors/{id}/archive. Archive permission; terminal state.</summary>
public sealed record ArchiveDonorCommand(Guid DonorId, ReasonRequest Request);

/// <summary>SCR-DON-003 Correct: record a change to an active donor with a named reason.</summary>
public sealed record CorrectDonorCommand(Guid DonorId, CorrectDonorRequest Request);

/// <summary>SCR-DON-003 Delete unused draft: only for a Prospect that nothing references.</summary>
public sealed record DeleteDonorDraftCommand(Guid DonorId, ReasonRequest Request);

/// <summary>
/// The whole Donor lifecycle in one handler.
///
/// Every method follows the same shape, which is deliberate: load, check the version, check the
/// state, apply the change, write the audit row, stage the outbox row, save once. If a rule
/// fails it returns a Result carrying the stable error code from section 11 — nothing throws,
/// so the controller never has to catch anything to produce the right status.
/// </summary>
public sealed class DonorCommandHandler(
    IDonorRepository donorRepository,
    IIdempotencyRepository idempotencyRepository,
    IReferenceNumberGenerator referenceNumbers,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    private const string CreateEndpoint = "POST /api/v1/donors";

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        CreateDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        // Section 10 idempotency. An importer that retries after a timeout must not create a
        // second donor, so the key it sent is looked up before anything else happens.
        if (!string.IsNullOrWhiteSpace(currentUser.IdempotencyKey))
        {
            var replay = await idempotencyRepository.FindAsync(currentUser.IdempotencyKey, CreateEndpoint, cancellationToken);
            if (replay is not null)
            {
                var existing = await donorRepository.GetByIdAsync(replay.ResourceId, cancellationToken);
                if (existing is not null)
                {
                    return Result.Success(BuildDetail(existing));
                }
            }
        }

        var businessKey = DonorMappingConfig.BuildBusinessKey(
            request.PrimaryEmail, request.PrimaryPhone, request.DonorType,
            request.FirstName, request.LastName, request.OrganisationName);

        if (await donorRepository.ExistsByBusinessKeyAsync(businessKey, null, cancellationToken))
        {
            return Result.Failure<DonorDetailResponse>(Error.Duplicate(
                "A donor with the same e-mail, phone or name already exists. Open the existing record or change the value."));
        }

        var donorNumber = string.IsNullOrWhiteSpace(request.DonorNumber)
            ? await referenceNumbers.NextDonorNumberAsync(cancellationToken)
            : request.DonorNumber.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(request.DonorNumber)
            && await donorRepository.DonorNumberExistsAsync(donorNumber, cancellationToken))
        {
            return Result.Failure<DonorDetailResponse>(Error.Duplicate(
                $"Donor number {donorNumber} is already in use."));
        }

        var donor = request.ToEntity(donorNumber, currentUser.OrganisationId);
        donor.RelationshipOwnerUserId = currentUser.UserId;
        donor.RelationshipOwnerName = currentUser.DisplayName;

        await donorRepository.AddAsync(donor, cancellationToken);

        if (!string.IsNullOrWhiteSpace(currentUser.IdempotencyKey))
        {
            idempotencyRepository.Add(new IdempotencyRecord
            {
                OrganisationId = currentUser.OrganisationId,
                Key = currentUser.IdempotencyKey,
                Endpoint = CreateEndpoint,
                ResourceId = donor.Id,
                ResourceReference = donor.DonorNumber
            });
        }

        var now = clock.UtcNow;

        // DonorCreatedDomainEvent -> audit row. DonorCreatedV1 -> outbox row. Both inside the
        // same SaveChanges as the donor itself, so the three can never disagree.
        var domainEvent = new DonorCreatedDomainEvent(
            donor.Id, donor.DonorNumber, donor.DonorType.ToString(), donor.Status.ToString(), now);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorCreated, nameof(Donor), donor.Id, AuditResult.Succeeded,
                $"{domainEvent.DonorNumber} created as {domainEvent.DonorType}."),
            cancellationToken);

        outboxWriter.Write(
            IntegrationEventNames.DonorCreatedV1, nameof(Donor), donor.Id,
            new DonorCreatedV1(donor.Id, donor.DonorNumber, donor.DonorType.ToString(), donor.OrganisationId, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        UpdateDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(scopeFailure);
        }

        if (command.Request.ExpectedVersion != donor.Version)
        {
            return Result.Failure<DonorDetailResponse>(Error.Concurrency());
        }

        if (donor.Status is DonorStatus.Archived or DonorStatus.Merged)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                $"A donor in state {donor.Status} can no longer be edited."));
        }

        var businessKey = DonorMappingConfig.BuildBusinessKey(
            command.Request.PrimaryEmail, command.Request.PrimaryPhone, command.Request.DonorType,
            command.Request.FirstName, command.Request.LastName, command.Request.OrganisationName);

        if (!string.Equals(businessKey, donor.NormalizedBusinessKey, StringComparison.Ordinal)
            && await donorRepository.ExistsByBusinessKeyAsync(businessKey, donor.Id, cancellationToken))
        {
            return Result.Failure<DonorDetailResponse>(Error.Duplicate(
                "Another donor already uses that e-mail, phone or name."));
        }

        command.Request.ApplyUpdate(donor);

        // DonorUpdatedDomainEvent stays inside the section: an edit is not a status change, and
        // section 10 only puts a fact on the outbox when another section actually needs it.
        var domainEvent = new DonorUpdatedDomainEvent(
            donor.Id, donor.DonorNumber, command.Request.ExpectedVersion, clock.UtcNow);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorUpdated, nameof(Donor), donor.Id, AuditResult.Succeeded,
                $"{domainEvent.DonorNumber} updated from version {domainEvent.Version}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        SubmitDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(scopeFailure);
        }

        var versionFailure = CheckVersion(donor, command.Request.ExpectedVersion);
        if (versionFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(versionFailure);
        }

        if (donor.ApprovalState == ApprovalState.PendingApproval)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                "This donor is already waiting for approval."));
        }

        if (donor.Status != DonorStatus.Prospect)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                $"Only a Prospect donor can be submitted. This donor is {donor.Status}."));
        }

        var now = clock.UtcNow;
        donor.ApprovalState = ApprovalState.PendingApproval;
        donor.SubmittedAtUtc = now;

        var domainEvent = new DonorSubmittedDomainEvent(donor.Id, donor.DonorNumber, currentUser.UserId, now);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorSubmitted, nameof(Donor), donor.Id, AuditResult.Succeeded,
                command.Request.Comment ?? $"{domainEvent.DonorNumber} submitted for approval."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        ApproveDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(scopeFailure);
        }

        var versionFailure = CheckVersion(donor, command.Request.ExpectedVersion);
        if (versionFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(versionFailure);
        }

        if (donor.ApprovalState != ApprovalState.PendingApproval)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                "Only a submitted donor can be approved or rejected."));
        }

        // Maker / checker. UI section 5.4: "The requester cannot silently act as an
        // independent approver." The creator is refused even when they hold the permission.
        if (donor.CreatedByUserId == currentUser.UserId)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.DonorApproved, nameof(Donor), donor.Id, AuditResult.Denied,
                    "Segregation of duties: the creator attempted to approve their own record."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<DonorDetailResponse>(Error.SegregationOfDuties(
                "You cannot approve a donor record you created. Ask a colleague to review it."));
        }

        var now = clock.UtcNow;
        var previousStatus = donor.Status.ToString();

        if (command.Request.Approved)
        {
            donor.ApprovalState = ApprovalState.Approved;
            donor.Status = DonorStatus.Active;
            donor.ApprovedAtUtc = now;
            donor.ApprovedByUserId = currentUser.UserId;
        }
        else
        {
            donor.ApprovalState = ApprovalState.Rejected;
        }

        var domainEvent = new DonorApprovedDomainEvent(
            donor.Id, donor.DonorNumber, command.Request.Approved, currentUser.UserId, now);

        await auditWriter.WriteAsync(
            new AuditEntry(
                command.Request.Approved ? AuditActionCodes.DonorApproved : AuditActionCodes.DonorRejected,
                nameof(Donor), donor.Id,
                command.Request.Approved ? AuditResult.Succeeded : AuditResult.Denied,
                command.Request.Reason ?? $"{domainEvent.DonorNumber} decision recorded."),
            cancellationToken);

        if (command.Request.Approved)
        {
            outboxWriter.Write(
                IntegrationEventNames.DonorStatusChangedV1, nameof(Donor), donor.Id,
                new DonorStatusChangedV1(donor.Id, donor.DonorNumber, previousStatus,
                    donor.Status.ToString(), donor.OrganisationId, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        CancelDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(scopeFailure);
        }

        var versionFailure = CheckVersion(donor, command.Request.ExpectedVersion);
        if (versionFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(versionFailure);
        }

        if (donor.Status is DonorStatus.Archived or DonorStatus.Merged or DonorStatus.Restricted)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                $"A donor in state {donor.Status} cannot be cancelled again."));
        }

        var now = clock.UtcNow;
        var previousStatus = donor.Status.ToString();

        // Cancel never deletes. The record moves to Restricted so its history, consents and
        // donations stay attached and readable.
        donor.Status = DonorStatus.Restricted;
        donor.ApprovalState = ApprovalState.Cancelled;
        donor.CancellationReason = command.Request.Reason.Trim();

        var domainEvent = new DonorCancelledDomainEvent(
            donor.Id, donor.DonorNumber, command.Request.Reason.Trim(), now);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorCancelled, nameof(Donor), donor.Id, AuditResult.Succeeded,
                domainEvent.Reason),
            cancellationToken);

        outboxWriter.Write(
            IntegrationEventNames.DonorStatusChangedV1, nameof(Donor), donor.Id,
            new DonorStatusChangedV1(donor.Id, donor.DonorNumber, previousStatus,
                donor.Status.ToString(), donor.OrganisationId, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result> HandleAsync(
        ArchiveDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure(scopeFailure);
        }

        var versionFailure = CheckVersion(donor, command.Request.ExpectedVersion);
        if (versionFailure is not null)
        {
            return Result.Failure(versionFailure);
        }

        if (donor.Status == DonorStatus.Archived)
        {
            return Result.Failure(Error.InvalidTransition("This donor is already archived."));
        }

        if (donor.Status == DonorStatus.Merged)
        {
            return Result.Failure(Error.InvalidTransition(
                "A merged donor is already terminal and cannot be archived separately."));
        }

        var now = clock.UtcNow;
        var previousStatus = donor.Status.ToString();

        donor.Status = DonorStatus.Archived;
        donor.ArchiveReason = command.Request.Reason.Trim();

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorArchived, nameof(Donor), donor.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        outboxWriter.Write(
            IntegrationEventNames.DonorStatusChangedV1, nameof(Donor), donor.Id,
            new DonorStatusChangedV1(donor.Id, donor.DonorNumber, previousStatus,
                donor.Status.ToString(), donor.OrganisationId, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        CorrectDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<DonorDetailResponse>(scopeFailure);
        }

        if (command.Request.ExpectedVersion != donor.Version)
        {
            return Result.Failure<DonorDetailResponse>(Error.Concurrency());
        }

        if (donor.Status is DonorStatus.Archived or DonorStatus.Merged)
        {
            return Result.Failure<DonorDetailResponse>(Error.InvalidTransition(
                $"A donor in state {donor.Status} can no longer be corrected."));
        }

        // The correct action is the safe half of edit: it touches the fields Donor 360 exposes
        // and always records why. Nothing here changes status or approval position.
        if (command.Request.FirstName is not null)
        {
            donor.FirstName = command.Request.FirstName.Trim();
        }

        if (command.Request.LastName is not null)
        {
            donor.LastName = command.Request.LastName.Trim();
        }

        if (command.Request.OrganisationName is not null)
        {
            donor.OrganisationName = command.Request.OrganisationName.Trim();
        }

        if (command.Request.PrimaryEmail is not null)
        {
            donor.PrimaryEmail = string.IsNullOrWhiteSpace(command.Request.PrimaryEmail)
                ? null
                : command.Request.PrimaryEmail.Trim().ToLowerInvariant();
        }

        if (command.Request.PrimaryPhone is not null)
        {
            donor.PrimaryPhone = string.IsNullOrWhiteSpace(command.Request.PrimaryPhone)
                ? null
                : command.Request.PrimaryPhone.Trim();
        }

        // THE RELATIONSHIP OWNER MOVES AS A PAIR. Setting the id without the name leaves the
        // grid printing the previous owner's name beside the new owner's work, which is worse
        // than showing nothing - so the name is taken from the request when it is supplied and
        // cleared when it is not, rather than left at whatever it held.
        if (command.Request.RelationshipOwnerUserId is not null)
        {
            donor.RelationshipOwnerUserId = command.Request.RelationshipOwnerUserId;
            donor.RelationshipOwnerName = string.IsNullOrWhiteSpace(command.Request.RelationshipOwnerName)
                ? null
                : command.Request.RelationshipOwnerName.Trim();
        }

        if (command.Request.PreferredLanguage is not null)
        {
            donor.PreferredLanguage = command.Request.PreferredLanguage.Trim();
        }

        if (command.Request.DoNotContact is not null)
        {
            donor.DoNotContact = command.Request.DoNotContact.Value;
        }

        if (command.Request.Notes is not null)
        {
            donor.Notes = command.Request.Notes.Trim();
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorCorrected, nameof(Donor), donor.Id, AuditResult.Succeeded,
                command.Request.CorrectionReason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(donor));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteDonorDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetWithChildrenAsync(command.DonorId, cancellationToken);
        if (donor is null)
        {
            return Result.Failure<OutcomeResponse>(Error.DonorNotFound());
        }

        var scopeFailure = CheckScope(donor);
        if (scopeFailure is not null)
        {
            return Result.Failure<OutcomeResponse>(scopeFailure);
        }

        // Section 9 deletion rule: permanent delete only for an unused draft with no child or
        // history reference. Anything else uses cancel or archive instead.
        if (donor.Status != DonorStatus.Prospect || donor.ApprovalState != ApprovalState.NotSubmitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Permanent delete is only available for an unsubmitted draft. Use cancel or archive instead."));
        }

        if (donor.Consents.Count > 0 || donor.Interactions.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This draft already has consent or interaction history and cannot be deleted. Use cancel instead."));
        }

        var reference = donor.DonorNumber;
        donorRepository.Remove(donor);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorDraftDeleted, nameof(Donor), donor.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            reference,
            "Deleted",
            clock.UtcNow,
            "The unused draft donor was permanently deleted.",
            "Return to the donor list",
            null,
            currentUser.CorrelationId));
    }

    private DonorDetailResponse BuildDetail(Donor donor) =>
        donor.ToDetailResponse(currentUser.CanSeeContact(), DonorMappingConfig.PermittedActionsFor(donor));

    /// <summary>
    /// Record scope. A caller restricted to their own records may not act on somebody else's
    /// donor, and the message is the same not-found text either way so the response never
    /// confirms that the record exists.
    /// </summary>
    private Error? CheckScope(Donor donor)
    {
        if (donor.OrganisationId != currentUser.OrganisationId)
        {
            return Error.DonorNotFound();
        }

        if (currentUser.Scope.IsOwnRecordsOnly && donor.RelationshipOwnerUserId != currentUser.UserId)
        {
            return Error.DonorNotFound();
        }

        return null;
    }

    /// <summary>ExpectedVersion is optional on the transition bodies, but honoured when sent.</summary>
    private static Error? CheckVersion(Donor donor, long? expectedVersion) =>
        expectedVersion is > 0 && expectedVersion != donor.Version ? Error.Concurrency() : null;
}
