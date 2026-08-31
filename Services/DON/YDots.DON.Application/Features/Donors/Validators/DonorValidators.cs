using FluentValidation;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Domain.Enums;
using YDots.DON.Domain.ValueObjects;

namespace YDots.DON.Application.Features.Donors.Validators;

/// <summary>
/// Validation for the Donor slice. The messages follow the content patterns in UI section
/// 4.x.6: "Enter {Field label}." for a missing value, "Review {Field label}." for a bad one.
/// </summary>
public sealed class CreateDonorRequestValidator : AbstractValidator<CreateDonorRequest>
{
    public CreateDonorRequestValidator()
    {
        RuleFor(request => request.DonorNumber)
            .Must(DonorNumberValue.IsValid)
            .WithMessage("Review Donor number. Use the format DON-YYYY-NNNNNN or leave it blank to generate one.")
            .When(request => !string.IsNullOrWhiteSpace(request.DonorNumber));

        RuleFor(request => request.DonorType)
            .IsInEnum().WithMessage("Enter Donor type.");

        // An individual needs a name; an organisation needs an organisation name. The property
        // contract states both, and an Anonymous donor deliberately needs neither.
        RuleFor(request => request.FirstName)
            .NotEmpty().WithMessage("Enter First name.")
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.DonorType == DonorType.Individual);

        RuleFor(request => request.LastName)
            .NotEmpty().WithMessage("Enter Last name.")
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.DonorType == DonorType.Individual);

        RuleFor(request => request.OrganisationName)
            .NotEmpty().WithMessage("Enter Organisation name.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.")
            .When(request => request.DonorType == DonorType.Organisation);

        RuleFor(request => request.PrimaryEmail)
            .Must(EmailValue.IsValid)
            .WithMessage("Review Email address. The value does not meet the stated format.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryEmail));

        RuleFor(request => request.PrimaryPhone)
            .Must(PrimaryPhoneValue.IsValid)
            .WithMessage("Review Mobile number. Use the international format, for example +919876543210.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryPhone));

        RuleFor(request => request.PreferredLanguage)
            .NotEmpty().WithMessage("Enter Preferred language.")
            .Must(SupportedLanguages.IsSupported)
            .WithMessage("Review Preferred language. Choose a value from the approved catalogue.");
    }
}

public sealed class UpdateDonorRequestValidator : AbstractValidator<UpdateDonorRequest>
{
    public UpdateDonorRequestValidator()
    {
        RuleFor(request => request.DonorType)
            .IsInEnum().WithMessage("Enter Donor type.");

        RuleFor(request => request.FirstName)
            .NotEmpty().WithMessage("Enter First name.")
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.DonorType == DonorType.Individual);

        RuleFor(request => request.LastName)
            .NotEmpty().WithMessage("Enter Last name.")
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.DonorType == DonorType.Individual);

        RuleFor(request => request.OrganisationName)
            .NotEmpty().WithMessage("Enter Organisation name.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.")
            .When(request => request.DonorType == DonorType.Organisation);

        RuleFor(request => request.PrimaryEmail)
            .Must(EmailValue.IsValid)
            .WithMessage("Review Email address. The value does not meet the stated format.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryEmail));

        RuleFor(request => request.PrimaryPhone)
            .Must(PrimaryPhoneValue.IsValid)
            .WithMessage("Review Mobile number. Use the international format, for example +919876543210.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryPhone));

        RuleFor(request => request.PreferredLanguage)
            .NotEmpty().WithMessage("Enter Preferred language.")
            .Must(SupportedLanguages.IsSupported)
            .WithMessage("Review Preferred language. Choose a value from the approved catalogue.");

        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("The record version is required. Reload the record if it is missing.");
    }
}
