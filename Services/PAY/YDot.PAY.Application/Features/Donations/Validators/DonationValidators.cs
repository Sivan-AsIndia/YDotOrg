using System.Text.RegularExpressions;
using FluentValidation;
using YDot.PAY.Application.Features.Donations.DTOs;

namespace YDot.PAY.Application.Features.Donations.Validators;

/// <summary>
/// Validators for the Donations slice.
///
/// THESE RUN AGAINST A STRANGER'S INPUT. The public donation form is reachable by anybody with a
/// QR code, so this is the outermost boundary of the system for the most consequential operation
/// it performs - and the messages are written for a member of the public rather than an
/// operator.
/// </summary>
public sealed class CreateDonationIntentRequestValidator : AbstractValidator<CreateDonationIntentRequest>
{
    /// <summary>
    /// A pragmatic address check, not RFC 5322.
    ///
    /// A full RFC-compliant pattern accepts addresses no mail server will deliver to and rejects
    /// nothing a donor would realistically type. What matters here is catching the typo before
    /// the receipt is sent somewhere that does not exist.
    /// </summary>
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public CreateDonationIntentRequestValidator()
    {
        RuleFor(request => request.DonorName)
            .NotEmpty().WithMessage("Please enter your name.")
            .MaximumLength(200);

        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("Please enter your e-mail address so we can send your receipt.")
            .MaximumLength(320)
            .Must(email => EmailPattern.IsMatch(email.Trim()))
            .WithMessage("That e-mail address does not look right. Please check it.");

        // GREATER THAN ZERO, not merely non-negative. A donation of nothing is not a donation,
        // and a gateway asked to take zero returns an error the donor cannot act on.
        RuleFor(request => request.Amount)
            .GreaterThan(0).WithMessage("Please enter an amount greater than zero.")
            .LessThanOrEqualTo(10_000_000)
            .WithMessage("That amount is unusually large. Please contact the organisation directly.");

        RuleFor(request => request.CurrencyCode)
            .NotEmpty().WithMessage("A currency is required.")
            .Length(3).WithMessage("Use a three-letter currency code, such as INR.")
            .Matches("^[A-Za-z]{3}$").WithMessage("A currency code contains letters only.");

        RuleFor(request => request.Mobile)
            .MaximumLength(20)
            .Matches(@"^[0-9+\-\s()]+$")
            .WithMessage("A phone number may contain digits, spaces, brackets, + and -.")
            .When(request => !string.IsNullOrWhiteSpace(request.Mobile));

        RuleFor(request => request.TaxIdentifier)
            .MaximumLength(30)
            .When(request => !string.IsNullOrWhiteSpace(request.TaxIdentifier));

        RuleFor(request => request.AddressLine1).MaximumLength(250);
        RuleFor(request => request.AddressLine2).MaximumLength(250);
        RuleFor(request => request.PostalCode).MaximumLength(20);
        RuleFor(request => request.TrackingReference).MaximumLength(64);
        RuleFor(request => request.PublicRecognitionName).MaximumLength(200);

        RuleFor(request => request.SourceType).IsInEnum();

        // A recognition name only means something if recognition was agreed to. Accepting one
        // without consent would leave a name we have no permission to display.
        RuleFor(request => request.PublicRecognitionName)
            .Empty()
            .When(request => !request.AllowPublicRecognition)
            .WithMessage("A public name can only be given when public recognition is agreed to.");
    }
}

/// <summary>Validator for asking for a payment link.</summary>
public sealed class CreatePaymentLinkRequestValidator : AbstractValidator<CreatePaymentLinkRequest>
{
    public CreatePaymentLinkRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.PreferredMethod).MaximumLength(40);
    }
}

/// <summary>Validator for cancelling an intent.</summary>
public sealed class CancelDonationIntentRequestValidator : AbstractValidator<CancelDonationIntentRequest>
{
    public CancelDonationIntentRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        // Mandatory: cancelling somebody's donation is an action that will be questioned.
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Explain why this donation is being cancelled.")
            .MaximumLength(1000);
    }
}

/// <summary>Validator for recording a donation taken outside the gateway.</summary>
public sealed class RecordOfflineDonationRequestValidator
    : AbstractValidator<RecordOfflineDonationRequest>
{
    public RecordOfflineDonationRequestValidator()
    {
        RuleFor(request => request.DonorName)
            .NotEmpty().WithMessage("Enter the donor's name.")
            .MaximumLength(200);

        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("Enter the donor's e-mail address.")
            .MaximumLength(320);

        RuleFor(request => request.Amount)
            .GreaterThan(0).WithMessage("Enter an amount greater than zero.");

        RuleFor(request => request.CurrencyCode)
            .NotEmpty().Length(3)
            .WithMessage("Use a three-letter currency code, such as INR.");

        RuleFor(request => request.MethodType).IsInEnum();

        // A donation dated in the future would fall into a financial year that has not started,
        // and its receipt would be numbered into a sequence that does not exist yet.
        RuleFor(request => request.ReceivedAtUtc)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddDays(1))
            .WithMessage("A donation cannot be dated in the future.");

        RuleFor(request => request.ExternalReference).MaximumLength(100);
        RuleFor(request => request.Notes).MaximumLength(2000);
    }
}

/// <summary>Validator for reconciling a donation.</summary>
public sealed class ReconcileDonationRequestValidator : AbstractValidator<ReconcileDonationRequest>
{
    public ReconcileDonationRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SettlementBatchReference).MaximumLength(100);
        RuleFor(request => request.Note).MaximumLength(1000);

        // A discrepancy with no note is a flag nobody can act on: the whole point of the state is
        // that a person has to decide which record is right.
        RuleFor(request => request.Note)
            .NotEmpty()
            .When(request => request.Status == Domain.Enums.ReconciliationStatus.Discrepancy)
            .WithMessage("Describe the discrepancy so somebody can resolve it.");
    }
}
