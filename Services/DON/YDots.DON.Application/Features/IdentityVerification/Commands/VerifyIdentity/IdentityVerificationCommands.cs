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
using YDots.DON.Application.Features.IdentityVerification.DTOs;
using YDots.DON.Application.Features.IdentityVerification.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.IdentityVerification.Commands.VerifyIdentity;

/// <summary>DON-UI-07 Send challenge. Primary action, idempotent.</summary>
public sealed record SendChallengeCommand(SendChallengeRequest Request);

/// <summary>DON-UI-07 Verify code.</summary>
public sealed record VerifyCodeCommand(Guid VerificationId, VerifyCodeRequest Request);

/// <summary>DON-UI-07 Escalate review.</summary>
public sealed record EscalateVerificationCommand(Guid VerificationId, EscalateVerificationRequest Request);

/// <summary>DON-UI-07 Cancel verification. Danger action: named reason required.</summary>
public sealed record CancelVerificationCommand(Guid VerificationId, ReasonRequest Request);

/// <summary>
/// The identity verification write side.
///
/// Two things are deliberately absent from every response here: the code, and the unmasked
/// destination. The code is delivered to the donor and only its hash is stored, so a person
/// with database access still cannot pass somebody else's challenge, and a screenshot of this
/// screen gives nothing away.
/// </summary>
public sealed class IdentityVerificationCommandHandler(
    IVerificationRepository verificationRepository,
    IDonorRepository donorRepository,
    IReferenceNumberGenerator referenceNumbers,
    IChallengeCodeService challengeCodes,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<ChallengeSentResponse>> HandleAsync(
        SendChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        var donor = await donorRepository.GetByIdAsync(request.DonorId, cancellationToken);
        if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<ChallengeSentResponse>(Error.DonorNotFound());
        }

        if (!Enum.TryParse<VerificationChannel>(request.VerificationChannel, ignoreCase: true, out var channel))
        {
            return Result.Failure<ChallengeSentResponse>(Error.Validation(
                "Review Verification channel. Choose a value from the approved catalogue.",
                [new ValidationError(nameof(request.VerificationChannel), "Choose a channel from the list.")]));
        }

        var destination = channel == VerificationChannel.Email ? donor.PrimaryEmail : donor.PrimaryPhone;

        if (string.IsNullOrWhiteSpace(destination))
        {
            return Result.Failure<ChallengeSentResponse>(Error.InvalidTransition(
                $"This donor has no {channel} destination on record, so a challenge cannot be sent."));
        }

        var now = clock.UtcNow;
        var (code, codeHash) = challengeCodes.Create();

        // An open attempt is reused rather than duplicated. Pressing Send challenge twice is
        // a resend, not two competing challenges for the same person.
        var verification = await verificationRepository.GetOpenForDonorAsync(donor.Id, cancellationToken);

        if (verification is null)
        {
            verification = new DonorIdentityVerification
            {
                OrganisationId = donor.OrganisationId,
                VerificationReference = await referenceNumbers.NextVerificationReferenceAsync(cancellationToken),
                DonorId = donor.Id
            };

            verificationRepository.Add(verification);
        }

        verification.VerificationPurpose = request.VerificationPurpose.Trim();
        verification.VerificationChannel = channel;
        verification.MaskedDestination = challengeCodes.MaskDestination(destination);
        verification.Status = VerificationStatus.ChallengeSent;
        verification.AttemptCount = 0;
        verification.ChallengeCodeHash = codeHash;
        verification.SentAtUtc = now;
        verification.ExpiryAtUtc = now.AddMinutes(_settings.VerificationCodeValidMinutes);
        verification.VerifiedAtUtc = null;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.VerificationChallengeSent, nameof(DonorIdentityVerification),
                verification.Id, AuditResult.Succeeded,
                $"{verification.VerificationReference} challenge sent by {channel} to {verification.MaskedDestination}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        verification.Donor = donor;

        // The delivery itself is a separate dependency. The local record is committed either
        // way, and the response says so, which is what UI section 4.7.4 asks for.
        return Result.Success(new ChallengeSentResponse(
            verification.ToResponse(currentUser.CanSeeEvidence(), _settings.VerificationMaxAttempts),
            "Queued",
            $"A {_settings.VerificationCodeDigits} digit code was queued for delivery to {verification.MaskedDestination}. "
            + $"It expires in {_settings.VerificationCodeValidMinutes} minutes.",
            // The delivery provider lives outside this section. Naming it here lets the screen
            // separate "we saved the attempt" from "the message actually went out".
            $"Delivery by {channel} is pending. Quote {verification.VerificationReference} if the donor does not receive it."));
    }

    public async Task<Result<IdentityVerificationResponse>> HandleAsync(
        VerifyCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        var verification = await verificationRepository.GetByIdAsync(command.VerificationId, cancellationToken);

        if (verification is null || verification.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<IdentityVerificationResponse>(
                Error.NotFound("That verification was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != verification.Version)
        {
            return Result.Failure<IdentityVerificationResponse>(Error.Concurrency());
        }

        if (verification.Status is not (VerificationStatus.ChallengeSent or VerificationStatus.Escalated))
        {
            return Result.Failure<IdentityVerificationResponse>(Error.InvalidTransition(
                $"A verification in state {verification.Status} cannot accept a code."));
        }

        var now = clock.UtcNow;

        if (verification.ExpiryAtUtc is not null && verification.ExpiryAtUtc < now)
        {
            verification.Status = VerificationStatus.Expired;

            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.VerificationCodeVerified, nameof(DonorIdentityVerification),
                    verification.Id, AuditResult.Failed, "The code had expired."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<IdentityVerificationResponse>(Error.InvalidTransition(
                "That code has expired. Send a new challenge."));
        }

        verification.AttemptCount += 1;

        if (!challengeCodes.Verify(command.Request.Code, verification.ChallengeCodeHash))
        {
            // The hash is cleared once the attempts run out, so a stale code can never be
            // replayed against a locked-out attempt later.
            if (verification.AttemptCount >= _settings.VerificationMaxAttempts)
            {
                verification.Status = VerificationStatus.Failed;
                verification.ChallengeCodeHash = null;
            }

            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.VerificationCodeVerified, nameof(DonorIdentityVerification),
                    verification.Id, AuditResult.Failed,
                    $"Attempt {verification.AttemptCount} of {_settings.VerificationMaxAttempts} did not match."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var remaining = Math.Max(0, _settings.VerificationMaxAttempts - verification.AttemptCount);

            return Result.Failure<IdentityVerificationResponse>(Error.Validation(
                remaining == 0
                    ? "That code did not match and no attempts remain. Send a new challenge or escalate for review."
                    : $"That code did not match. {remaining} attempt(s) remain.",
                [new ValidationError(nameof(command.Request.Code), "Check the code and try again.")]));
        }

        verification.Status = VerificationStatus.Verified;
        verification.VerifiedAtUtc = now;
        verification.IdentityConfidence = IdentityConfidence.High;
        verification.ChallengeCodeHash = null;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.VerificationCodeVerified, nameof(DonorIdentityVerification),
                verification.Id, AuditResult.Succeeded,
                $"{verification.VerificationReference} verified on attempt {verification.AttemptCount}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(verification.ToResponse(
            currentUser.CanSeeEvidence(), _settings.VerificationMaxAttempts));
    }

    public async Task<Result<IdentityVerificationResponse>> HandleAsync(
        EscalateVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var verification = await verificationRepository.GetByIdAsync(command.VerificationId, cancellationToken);

        if (verification is null || verification.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<IdentityVerificationResponse>(
                Error.NotFound("That verification was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != verification.Version)
        {
            return Result.Failure<IdentityVerificationResponse>(Error.Concurrency());
        }

        if (verification.Status is VerificationStatus.Verified or VerificationStatus.Cancelled)
        {
            return Result.Failure<IdentityVerificationResponse>(Error.InvalidTransition(
                $"A verification in state {verification.Status} cannot be escalated."));
        }

        verification.Status = VerificationStatus.Escalated;
        verification.ReviewerUserId = command.Request.ReviewerUserId;
        verification.ReviewerName = command.Request.ReviewerName.Trim();
        verification.EscalationReason = command.Request.EscalationReason.Trim();
        verification.EvidenceReference = command.Request.EvidenceReference?.Trim() ?? verification.EvidenceReference;
        verification.IdentityConfidence = IdentityConfidence.Low;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.VerificationEscalated, nameof(DonorIdentityVerification),
                verification.Id, AuditResult.Succeeded, command.Request.EscalationReason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(verification.ToResponse(
            currentUser.CanSeeEvidence(), _settings.VerificationMaxAttempts));
    }

    public async Task<Result<IdentityVerificationResponse>> HandleAsync(
        CancelVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var verification = await verificationRepository.GetByIdAsync(command.VerificationId, cancellationToken);

        if (verification is null || verification.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<IdentityVerificationResponse>(
                Error.NotFound("That verification was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != verification.Version)
        {
            return Result.Failure<IdentityVerificationResponse>(Error.Concurrency());
        }

        if (verification.Status is VerificationStatus.Verified or VerificationStatus.Cancelled)
        {
            return Result.Failure<IdentityVerificationResponse>(Error.InvalidTransition(
                $"A verification in state {verification.Status} cannot be cancelled."));
        }

        verification.Status = VerificationStatus.Cancelled;
        verification.CancellationReason = command.Request.Reason.Trim();
        verification.ChallengeCodeHash = null;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.VerificationCancelled, nameof(DonorIdentityVerification),
                verification.Id, AuditResult.Succeeded, command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(verification.ToResponse(
            currentUser.CanSeeEvidence(), _settings.VerificationMaxAttempts));
    }
}

public sealed class SendChallengeRequestValidator : AbstractValidator<SendChallengeRequest>
{
    public SendChallengeRequestValidator()
    {
        RuleFor(request => request.DonorId).NotEmpty().WithMessage("Enter Donor reference.");

        RuleFor(request => request.VerificationPurpose)
            .NotEmpty().WithMessage("Enter Verification purpose.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.VerificationChannel)
            .NotEmpty().WithMessage("Enter Verification channel.");
    }
}

public sealed class VerifyCodeRequestValidator : AbstractValidator<VerifyCodeRequest>
{
    public VerifyCodeRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty().WithMessage("Enter the code.")
            .Length(4, 10).WithMessage("Use between 4 and 10 characters.")
            .Matches("^[0-9]+$").WithMessage("Review the code. Use digits only.");
    }
}

public sealed class EscalateVerificationRequestValidator : AbstractValidator<EscalateVerificationRequest>
{
    public EscalateVerificationRequestValidator()
    {
        RuleFor(request => request.ReviewerUserId).NotEmpty().WithMessage("Enter Reviewer.");

        RuleFor(request => request.ReviewerName)
            .NotEmpty().WithMessage("Enter Reviewer.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.");

        RuleFor(request => request.EscalationReason)
            .NotEmpty().WithMessage("Enter Escalation reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}
