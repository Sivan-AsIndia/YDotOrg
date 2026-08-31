using FluentValidation;
using YDot.PAY.Application.Features.Receipts.DTOs;

namespace YDot.PAY.Application.Features.Receipts.Validators;

/// <summary>Validator for issuing a receipt.</summary>
public sealed class IssueReceiptRequestValidator : AbstractValidator<IssueReceiptRequest>
{
    public IssueReceiptRequestValidator()
    {
        RuleFor(request => request.OrganisationTaxReference).MaximumLength(50);
        RuleFor(request => request.TaxExemptionReference).MaximumLength(100);
    }
}

/// <summary>Validator for correcting a receipt.</summary>
public sealed class CorrectReceiptRequestValidator : AbstractValidator<CorrectReceiptRequest>
{
    public CorrectReceiptRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        // Mandatory. A corrected tax document with no stated reason is exactly what an auditor
        // asks about, and "we do not know" is not an answer.
        RuleFor(request => request.CorrectionReason)
            .NotEmpty().WithMessage("Explain what is being corrected and why.")
            .MaximumLength(1000);

        RuleFor(request => request.DonorName).MaximumLength(200);
        RuleFor(request => request.DonorAddress).MaximumLength(500);
        RuleFor(request => request.DonorTaxIdentifier).MaximumLength(30);
    }
}

/// <summary>Validator for voiding a receipt.</summary>
public sealed class VoidReceiptRequestValidator : AbstractValidator<VoidReceiptRequest>
{
    public VoidReceiptRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Explain why this receipt is being voided.")
            .MaximumLength(1000);
    }
}

/// <summary>Validator for resending a receipt.</summary>
public sealed class ResendReceiptRequestValidator : AbstractValidator<ResendReceiptRequest>
{
    public ResendReceiptRequestValidator()
    {
        RuleFor(request => request.Channel)
            .NotEmpty()
            .Must(channel => channel is "Email" or "Sms" or "Post")
            .WithMessage("Send a receipt by Email, Sms or Post.");

        RuleFor(request => request.Destination)
            .MaximumLength(320)
            .When(request => !string.IsNullOrWhiteSpace(request.Destination));
    }
}
