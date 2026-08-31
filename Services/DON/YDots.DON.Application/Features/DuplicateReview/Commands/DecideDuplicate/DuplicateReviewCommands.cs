using FluentValidation;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.DuplicateReview.DTOs;
using YDots.DON.Application.Features.DuplicateReview.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.Events;

namespace YDots.DON.Application.Features.DuplicateReview.Commands.DecideDuplicate;

/// <summary>Raise a duplicate review for two candidate donors.</summary>
public sealed record CreateDuplicateReviewCommand(CreateDuplicateReviewRequest Request);

/// <summary>SCR-DON-004 Merge. Records the decision and, for a real merge, applies it.</summary>
public sealed record MergeDuplicateCommand(Guid ReviewId, MergeDecisionRequest Request);

/// <summary>SCR-DON-004 Reject candidate. Danger action: the pair is recorded as not a match.</summary>
public sealed record RejectDuplicateCandidateCommand(Guid ReviewId, ReasonRequest Request);

/// <summary>
/// The duplicate review write side.
///
/// A merge is irreversible in practice, so the handler does three things before it commits:
/// it re-checks that the surviving record is one of the two candidates, it moves the children
/// across rather than deleting them, and it leaves the absorbed record in place with status
/// Merged. Nothing is destroyed — section 9: "Permanent delete only for unused Draft".
/// </summary>
public sealed class DuplicateReviewCommandHandler(
    IDonorMergeCaseRepository mergeCaseRepository,
    IDonorRepository donorRepository,
    IConsentRepository consentRepository,
    IReferenceNumberGenerator referenceNumbers,
    IAuditWriter auditWriter,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    public async Task<Result<DuplicateReviewDetailResponse>> HandleAsync(
        CreateDuplicateReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        if (request.CandidateADonorId == request.CandidateBDonorId)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.Validation(
                "Review Candidate B. A record cannot be a duplicate of itself.",
                [new ValidationError(nameof(request.CandidateBDonorId), "Choose a different candidate.")]));
        }

        var candidateA = await donorRepository.GetByIdAsync(request.CandidateADonorId, cancellationToken);
        var candidateB = await donorRepository.GetByIdAsync(request.CandidateBDonorId, cancellationToken);

        if (candidateA is null || candidateA.OrganisationId != currentUser.OrganisationId
            || candidateB is null || candidateB.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.DonorNotFound());
        }

        if (await mergeCaseRepository.PairExistsAsync(candidateA.Id, candidateB.Id, cancellationToken))
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.Duplicate(
                "A review already exists for these two records."));
        }

        var confidence = Enum.TryParse<IdentityConfidence>(request.IdentityConfidence, ignoreCase: true, out var parsed)
            ? parsed
            : IdentityConfidence.Medium;

        var mergeCase = new DonorMergeCase
        {
            OrganisationId = currentUser.OrganisationId,
            ReviewReference = await referenceNumbers.NextMergeCaseReferenceAsync(cancellationToken),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = DonorMergeCaseStatus.Active,
            CandidateADonorId = candidateA.Id,
            CandidateBDonorId = candidateB.Id,
            IdentityConfidence = confidence,
            MatchingEvidence = request.MatchingEvidence?.Trim(),
            ContactComparison = DuplicateReviewMappingConfig.BuildContactComparison(candidateA, candidateB),
            ConflictingFields = DuplicateReviewMappingConfig.BuildConflictingFields(candidateA, candidateB),
            DonationHistoryImpact = "A merge moves the donation history of the absorbed record onto the surviving record.",
            ConsentImpact = "A merge moves every consent row onto the surviving record. Withdrawn channels stay withdrawn.",
            MergePreview = DuplicateReviewMappingConfig.BuildMergePreview(candidateA, candidateB)
        };

        mergeCaseRepository.Add(mergeCase);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.MergeCaseCreated, nameof(DonorMergeCase), mergeCase.Id,
                AuditResult.Succeeded,
                $"{mergeCase.ReviewReference} raised for {candidateA.DonorNumber} and {candidateB.DonorNumber}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        mergeCase.CandidateADonor = candidateA;
        mergeCase.CandidateBDonor = candidateB;

        return Result.Success(BuildDetail(mergeCase));
    }

    public async Task<Result<DuplicateReviewDetailResponse>> HandleAsync(
        MergeDuplicateCommand command,
        CancellationToken cancellationToken = default)
    {
        var mergeCase = await mergeCaseRepository.GetWithCandidatesAsync(command.ReviewId, cancellationToken);

        if (mergeCase is null || mergeCase.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(
                Error.NotFound("That duplicate review was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != mergeCase.Version)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.Concurrency());
        }

        if (mergeCase.Status is not (DonorMergeCaseStatus.Active or DonorMergeCaseStatus.UnderReview))
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.InvalidTransition(
                $"This review is already {mergeCase.Status} and cannot be decided again."));
        }

        if (!Enum.TryParse<MergeDecision>(command.Request.Decision, ignoreCase: true, out var decision)
            || decision == MergeDecision.Reject)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.Validation(
                "Review Decision. Choose Merge, Link or KeepSeparate.",
                [new ValidationError(nameof(command.Request.Decision), "Choose a decision from the list.")]));
        }

        var now = clock.UtcNow;

        mergeCase.Decision = decision;
        mergeCase.DecisionReason = command.Request.DecisionReason.Trim();
        mergeCase.DecidedByUserId = currentUser.UserId;
        mergeCase.DecidedByName = currentUser.DisplayName;
        mergeCase.DecidedAtUtc = now;

        if (decision == MergeDecision.Merge)
        {
            var mergeResult = await ApplyMergeAsync(mergeCase, command.Request.SurvivingDonorId, now, cancellationToken);
            if (mergeResult is not null)
            {
                return Result.Failure<DuplicateReviewDetailResponse>(mergeResult);
            }

            mergeCase.Status = DonorMergeCaseStatus.Merged;
        }
        else
        {
            mergeCase.Status = decision == MergeDecision.Link
                ? DonorMergeCaseStatus.Linked
                : DonorMergeCaseStatus.KeptSeparate;
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.MergeCaseMerged, nameof(DonorMergeCase), mergeCase.Id,
                AuditResult.Succeeded,
                $"{mergeCase.ReviewReference} decided as {decision}. {mergeCase.DecisionReason}"),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(mergeCase));
    }

    public async Task<Result<DuplicateReviewDetailResponse>> HandleAsync(
        RejectDuplicateCandidateCommand command,
        CancellationToken cancellationToken = default)
    {
        var mergeCase = await mergeCaseRepository.GetWithCandidatesAsync(command.ReviewId, cancellationToken);

        if (mergeCase is null || mergeCase.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(
                Error.NotFound("That duplicate review was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != mergeCase.Version)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.Concurrency());
        }

        if (mergeCase.Status is not (DonorMergeCaseStatus.Active or DonorMergeCaseStatus.UnderReview))
        {
            return Result.Failure<DuplicateReviewDetailResponse>(Error.InvalidTransition(
                $"This review is already {mergeCase.Status} and cannot be rejected."));
        }

        mergeCase.Status = DonorMergeCaseStatus.Rejected;
        mergeCase.Decision = MergeDecision.Reject;
        mergeCase.DecisionReason = command.Request.Reason.Trim();
        mergeCase.DecidedByUserId = currentUser.UserId;
        mergeCase.DecidedByName = currentUser.DisplayName;
        mergeCase.DecidedAtUtc = clock.UtcNow;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.MergeCaseRejected, nameof(DonorMergeCase), mergeCase.Id,
                AuditResult.Succeeded, command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BuildDetail(mergeCase));
    }

    /// <summary>
    /// Applies a merge. Everything the absorbed record owns is repointed at the survivor and
    /// the absorbed record itself stays in the table with status Merged, so nothing that ever
    /// referenced it breaks and its own history remains readable.
    /// </summary>
    private async Task<Error?> ApplyMergeAsync(
        DonorMergeCase mergeCase,
        Guid? survivingDonorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (survivingDonorId is null)
        {
            return Error.Validation("Enter Surviving record. A merge has to name the record that stays.");
        }

        if (survivingDonorId != mergeCase.CandidateADonorId && survivingDonorId != mergeCase.CandidateBDonorId)
        {
            return Error.Validation("Review Surviving record. It has to be candidate A or candidate B.");
        }

        var absorbedId = survivingDonorId == mergeCase.CandidateADonorId
            ? mergeCase.CandidateBDonorId
            : mergeCase.CandidateADonorId;

        var surviving = await donorRepository.GetWithChildrenAsync(survivingDonorId.Value, cancellationToken);
        var absorbed = await donorRepository.GetWithChildrenAsync(absorbedId, cancellationToken);

        if (surviving is null || absorbed is null)
        {
            return Error.DonorNotFound();
        }

        if (absorbed.Status == DonorStatus.Merged)
        {
            return Error.InvalidTransition($"{absorbed.DonorNumber} has already been merged into another record.");
        }

        // Fill the gaps on the survivor. An existing value is never overwritten: the steward
        // chose which record survives, so its own values win.
        surviving.PrimaryEmail ??= absorbed.PrimaryEmail;
        surviving.PrimaryPhone ??= absorbed.PrimaryPhone;
        surviving.FirstName ??= absorbed.FirstName;
        surviving.LastName ??= absorbed.LastName;
        surviving.OrganisationName ??= absorbed.OrganisationName;
        surviving.RelationshipOwnerUserId ??= absorbed.RelationshipOwnerUserId;
        surviving.RelationshipOwnerName ??= absorbed.RelationshipOwnerName;

        // Do not contact is the restrictive value, so it carries across in one direction only.
        surviving.DoNotContact = surviving.DoNotContact || absorbed.DoNotContact;

        foreach (var contact in absorbed.Contacts)
        {
            contact.DonorId = surviving.Id;
            contact.IsPrimary = false;
        }

        foreach (var interaction in absorbed.Interactions)
        {
            interaction.DonorId = surviving.Id;
        }

        foreach (var tag in absorbed.Tags)
        {
            tag.DonorId = surviving.Id;
        }

        var absorbedConsents = await consentRepository.GetHistoryAsync(absorbed.Id, cancellationToken);
        foreach (var consent in absorbedConsents)
        {
            consent.DonorId = surviving.Id;
        }

        var previousStatus = absorbed.Status.ToString();
        absorbed.Status = DonorStatus.Merged;
        absorbed.MergedIntoDonorId = surviving.Id;

        mergeCase.SurvivingDonorId = surviving.Id;
        mergeCase.MergePreview = DuplicateReviewMappingConfig.BuildMergePreview(surviving, absorbed);

        outboxWriter.Write(
            IntegrationEventNames.DonorStatusChangedV1, nameof(Donor), absorbed.Id,
            new DonorStatusChangedV1(absorbed.Id, absorbed.DonorNumber, previousStatus,
                absorbed.Status.ToString(), absorbed.OrganisationId, now));

        return null;
    }

    private DuplicateReviewDetailResponse BuildDetail(DonorMergeCase mergeCase) =>
        mergeCase.ToDetailResponse(
            currentUser.CanSeeContact(),
            currentUser.CanSeeEvidence(),
            DuplicateReviewMappingConfig.PermittedActionsFor(mergeCase));
}

public sealed class CreateDuplicateReviewRequestValidator : AbstractValidator<CreateDuplicateReviewRequest>
{
    public CreateDuplicateReviewRequestValidator()
    {
        RuleFor(request => request.CandidateADonorId).NotEmpty().WithMessage("Enter Candidate A.");
        RuleFor(request => request.CandidateBDonorId).NotEmpty().WithMessage("Enter Candidate B.");

        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter Name.")
            .Length(2, 160).WithMessage("Use between 2 and 160 characters.");

        RuleFor(request => request.Description)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.Description));

        RuleFor(request => request.MatchingEvidence)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.MatchingEvidence));
    }
}

public sealed class MergeDecisionRequestValidator : AbstractValidator<MergeDecisionRequest>
{
    public MergeDecisionRequestValidator()
    {
        RuleFor(request => request.Decision)
            .NotEmpty().WithMessage("Enter Decision.");

        RuleFor(request => request.DecisionReason)
            .NotEmpty().WithMessage("Enter Decision reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.SurvivingDonorId)
            .NotEmpty().WithMessage("Enter Surviving record.")
            .When(request => string.Equals(request.Decision, nameof(MergeDecision.Merge), StringComparison.OrdinalIgnoreCase));
    }
}
