using FluentValidation;
using YDot.PAY.Application.Features.Payments.DTOs;

namespace YDot.PAY.Application.Features.Payments.Validators;

/// <summary>Validator for a verification request.</summary>
public sealed class VerifyPaymentRequestValidator : AbstractValidator<VerifyPaymentRequest>
{
    public VerifyPaymentRequestValidator()
    {
        // One or the other, never neither. The public result page has a reference; staff have an
        // id. A request with neither has not said what to verify.
        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.IntentReference)
                             || request.PaymentAttemptId.HasValue)
            .WithName(nameof(VerifyPaymentRequest.IntentReference))
            .WithMessage("Name either a donation reference or a payment attempt.");

        RuleFor(request => request.IntentReference)
            .MaximumLength(64)
            .When(request => !string.IsNullOrWhiteSpace(request.IntentReference));
    }
}

/// <summary>Validator for reprocessing a queued event.</summary>
public sealed class ReprocessPaymentEventRequestValidator
    : AbstractValidator<ReprocessPaymentEventRequest>
{
    public ReprocessPaymentEventRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Note).MaximumLength(1000);
    }
}

/// <summary>Validator for dismissing a queued event.</summary>
public sealed class DismissPaymentEventRequestValidator : AbstractValidator<DismissPaymentEventRequest>
{
    public DismissPaymentEventRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        // Mandatory: dismissing a payment event is deciding that money needs no action, which is
        // exactly the decision somebody may have to justify.
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Explain why this event needs no action.")
            .MaximumLength(1000);
    }
}

/// <summary>Validator for a safe retry.</summary>
public sealed class SafeRetryRequestValidator : AbstractValidator<SafeRetryRequest>
{
    public SafeRetryRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Explain why this payment is being retried.")
            .MaximumLength(1000);
    }
}
