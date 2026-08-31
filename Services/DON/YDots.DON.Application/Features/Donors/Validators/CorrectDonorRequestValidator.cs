using FluentValidation;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Features.Donors.DTOs;

namespace YDots.DON.Application.Features.Donors.Validators;

public sealed class CorrectDonorRequestValidator : AbstractValidator<CorrectDonorRequest>
{
    public CorrectDonorRequestValidator()
    {
        RuleFor(request => request.CorrectionReason)
            .NotEmpty().WithMessage("Enter Correction reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");

        RuleFor(request => request.FirstName)
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.FirstName is not null);

        RuleFor(request => request.LastName)
            .MaximumLength(100).WithMessage("Use no more than 100 characters.")
            .When(request => request.LastName is not null);

        RuleFor(request => request.OrganisationName)
            .MaximumLength(200).WithMessage("Use no more than 200 characters.")
            .When(request => request.OrganisationName is not null);

        RuleFor(request => request.PrimaryEmail)
            .MaximumLength(320).WithMessage("Use no more than 320 characters.")
            .EmailAddress().WithMessage("Review E-mail. Enter a valid address.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryEmail));

        RuleFor(request => request.PrimaryPhone)
            .MaximumLength(32).WithMessage("Use no more than 32 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryPhone));

        // A name without an id would leave the record labelled with somebody the platform cannot
        // route work to, which is the state the donor grid used to be in for every row.
        RuleFor(request => request.RelationshipOwnerName)
            .NotEmpty().WithMessage("Enter Relationship owner.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.")
            .When(request => request.RelationshipOwnerUserId is not null);

        RuleFor(request => request.PreferredLanguage)
            .Must(SupportedLanguages.IsSupported)
            .WithMessage("Review Preferred language. Choose a value from the approved catalogue.")
            .When(request => request.PreferredLanguage is not null);

        RuleFor(request => request.Notes)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => request.Notes is not null);

        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("The record version is required. Reload the record if it is missing.");
    }
}
