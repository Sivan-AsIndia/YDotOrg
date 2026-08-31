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
using YDots.DON.Application.Features.ConsentCentre.DTOs;
using YDots.DON.Application.Features.ConsentCentre.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.ConsentCentre.Commands.ManageConsent;

/// <summary>SCR-DON-005 Grant. Records a new permission for one donor and channel.</summary>
public sealed record GrantConsentCommand(GrantConsentRequest Request);

/// <summary>SCR-DON-005 Withdraw. Closes the current permission for one channel.</summary>
public sealed record WithdrawConsentCommand(Guid ConsentId, WithdrawConsentRequest Request);

/// <summary>SCR-DON-005 Correct. Supersedes a row with a corrected copy.</summary>
public sealed record CorrectConsentCommand(Guid ConsentId, CorrectConsentRequest Request);

/// <summary>
/// The consent write side.
///
/// The one rule that shapes all three methods: consent rows are never edited. Grant supersedes
/// the previous row for the same channel, Withdraw closes it and inserts the opposite decision,
/// and Correct inserts a corrected copy. That is what makes "Consent history" a real history
/// rather than a list of whatever the last person typed.
/// </summary>
public sealed class ConsentCommandHandler(
    IConsentRepository consentRepository,
    IDonorRepository donorRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<ConsentListItemResponse>> HandleAsync(
        GrantConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        var donor = await donorRepository.GetByIdAsync(request.DonorId, cancellationToken);
        if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<ConsentListItemResponse>(Error.DonorNotFound());
        }

        if (donor.Status is DonorStatus.Archived or DonorStatus.Merged)
        {
            return Result.Failure<ConsentListItemResponse>(Error.InvalidTransition(
                $"A donor in state {donor.Status} cannot record new consent."));
        }

        if (!Enum.TryParse<ConsentChannel>(request.Channel, ignoreCase: true, out var channel))
        {
            return Result.Failure<ConsentListItemResponse>(Error.Validation(
                "Review Channel. Choose a value from the approved catalogue.",
                [new ValidationError(nameof(request.Channel), "Choose a channel from the list.")]));
        }

        if (request.ExpiryAtUtc is not null && request.ExpiryAtUtc <= request.EffectiveAtUtc)
        {
            return Result.Failure<ConsentListItemResponse>(Error.Validation(
                "Review Expiry time. It has to be later than the effective time.",
                [new ValidationError(nameof(request.ExpiryAtUtc), "Choose a later date and time.")]));
        }

        // Supersede whatever was current for this channel. Two Active rows for the same channel
        // would make "may we e-mail this person?" ambiguous, which is the one thing it must not be.
        var current = await consentRepository.GetCurrentAsync(donor.Id, channel, cancellationToken);

        var consent = new Consent
        {
            DonorId = donor.Id,
            OrganisationId = donor.OrganisationId,
            Name = $"{channel} consent - {donor.DonorNumber}",
            Description = request.Description?.Trim(),
            Status = ConsentStatus.Active,
            Purpose = request.Purpose.Trim(),
            Channel = channel,
            ConsentState = ConsentState.Granted,
            NoticeVersion = _settings.CurrentNoticeVersion,
            EvidenceSource = request.EvidenceSource.Trim(),
            EvidenceReference = request.EvidenceReference?.Trim(),
            EffectiveAtUtc = request.EffectiveAtUtc,
            ExpiryAtUtc = request.ExpiryAtUtc,
            PublicRecognitionPreference = request.PublicRecognitionPreference,
            ContactRestrictions = request.ContactRestrictions?.Trim(),
            CapturedByUserId = currentUser.UserId,
            CapturedByName = currentUser.DisplayName
        };

        consentRepository.Add(consent);

        if (current is not null)
        {
            current.Status = ConsentStatus.Superseded;
            current.SupersededByConsentId = consent.Id;
        }

        // Granting any channel contradicts a blanket "do not contact", so the flag is cleared
        // rather than left to quietly override the permission the donor just gave.
        if (donor.DoNotContact)
        {
            donor.DoNotContact = false;
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.ConsentGranted, nameof(Consent), consent.Id, AuditResult.Succeeded,
                $"{channel} consent granted for {donor.DonorNumber} under notice {consent.NoticeVersion}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        consent.Donor = donor;

        return Result.Success(consent.ToListItemResponse(currentUser.CanSeeEvidence()));
    }

    public async Task<Result<ConsentListItemResponse>> HandleAsync(
        WithdrawConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        var consent = await consentRepository.GetByIdAsync(command.ConsentId, cancellationToken);

        if (consent is null || consent.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<ConsentListItemResponse>(
                Error.NotFound("That consent record was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != consent.Version)
        {
            return Result.Failure<ConsentListItemResponse>(Error.Concurrency());
        }

        if (consent.Status != ConsentStatus.Active)
        {
            return Result.Failure<ConsentListItemResponse>(Error.InvalidTransition(
                $"Only an active consent can be withdrawn. This record is {consent.Status}."));
        }

        var now = command.Request.EffectiveAtUtc ?? clock.UtcNow;

        consent.Status = ConsentStatus.Withdrawn;
        consent.ConsentState = ConsentState.Withdrawn;
        consent.WithdrawnAtUtc = now;
        consent.WithdrawalReason = command.Request.Reason.Trim();

        // A withdrawal on every channel is the same fact as "do not contact", so the donor
        // record is brought into line instead of being left to disagree with its own consents.
        if (consent.DonorId is not null)
        {
            var remaining = await consentRepository.GetCurrentForDonorAsync(consent.DonorId.Value, cancellationToken);

            if (remaining.All(row => row.Id == consent.Id || row.ConsentState != ConsentState.Granted))
            {
                var donor = await donorRepository.GetByIdAsync(consent.DonorId.Value, cancellationToken);
                if (donor is not null)
                {
                    donor.DoNotContact = true;
                }
            }
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.ConsentWithdrawn, nameof(Consent), consent.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(consent.ToListItemResponse(currentUser.CanSeeEvidence()));
    }

    public async Task<Result<ConsentListItemResponse>> HandleAsync(
        CorrectConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        var original = await consentRepository.GetByIdAsync(command.ConsentId, cancellationToken);

        if (original is null || original.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<ConsentListItemResponse>(
                Error.NotFound("That consent record was not found inside your scope."));
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != original.Version)
        {
            return Result.Failure<ConsentListItemResponse>(Error.Concurrency());
        }

        if (original.Status == ConsentStatus.Superseded)
        {
            return Result.Failure<ConsentListItemResponse>(Error.InvalidTransition(
                "This record has already been superseded. Correct the current row instead."));
        }

        var corrected = original.ToCorrectedCopy(command.Request, _settings.CurrentNoticeVersion);
        corrected.CapturedByUserId = currentUser.UserId;
        corrected.CapturedByName = currentUser.DisplayName;

        if (corrected.ExpiryAtUtc is not null && corrected.ExpiryAtUtc <= corrected.EffectiveAtUtc)
        {
            return Result.Failure<ConsentListItemResponse>(Error.Validation(
                "Review Expiry time. It has to be later than the effective time.",
                [new ValidationError(nameof(command.Request.ExpiryAtUtc), "Choose a later date and time.")]));
        }

        consentRepository.Add(corrected);

        original.Status = ConsentStatus.Superseded;
        original.SupersededByConsentId = corrected.Id;

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.ConsentCorrected, nameof(Consent), corrected.Id, AuditResult.Succeeded,
                $"Corrected {original.Id}. {command.Request.CorrectionReason.Trim()}"),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        corrected.Donor = original.Donor;

        return Result.Success(corrected.ToListItemResponse(currentUser.CanSeeEvidence()));
    }
}

public sealed class GrantConsentRequestValidator : AbstractValidator<GrantConsentRequest>
{
    public GrantConsentRequestValidator()
    {
        RuleFor(request => request.DonorId).NotEmpty().WithMessage("Enter Donor reference.");

        RuleFor(request => request.Purpose)
            .NotEmpty().WithMessage("Enter Purpose.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.Channel).NotEmpty().WithMessage("Enter Channel.");

        RuleFor(request => request.EvidenceSource)
            .NotEmpty().WithMessage("Enter Evidence source.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.");

        RuleFor(request => request.EffectiveAtUtc)
            .NotEmpty().WithMessage("Enter Effective time.");

        RuleFor(request => request.ContactRestrictions)
            .MaximumLength(300).WithMessage("Use no more than 300 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.ContactRestrictions));

        RuleFor(request => request.Description)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.Description));
    }
}

public sealed class WithdrawConsentRequestValidator : AbstractValidator<WithdrawConsentRequest>
{
    public WithdrawConsentRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Enter Reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}

public sealed class CorrectConsentRequestValidator : AbstractValidator<CorrectConsentRequest>
{
    public CorrectConsentRequestValidator()
    {
        RuleFor(request => request.CorrectionReason)
            .NotEmpty().WithMessage("Enter Correction reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.Purpose)
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.Purpose));

        RuleFor(request => request.EvidenceSource)
            .MaximumLength(200).WithMessage("Use no more than 200 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.EvidenceSource));
    }
}
