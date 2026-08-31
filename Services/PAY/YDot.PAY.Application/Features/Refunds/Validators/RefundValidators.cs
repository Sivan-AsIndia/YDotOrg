using FluentValidation;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Refunds.Validators;

/// <summary>Validator for raising a refund.</summary>
public sealed class RequestRefundRequestValidator : AbstractValidator<RequestRefundRequest>
{
    public RequestRefundRequestValidator()
    {
        // The upper bound is the donation's refundable balance, which only the handler can know -
        // so this checks the shape and the handler checks the amount.
        RuleFor(request => request.Amount)
            .GreaterThan(0).WithMessage("Enter an amount greater than zero.");

        RuleFor(request => request.Reason).IsInEnum();

        // Other with no detail is not a reason, it is a shrug - and refund reasons are reported
        // on, so an unexplained Other is a hole in that report.
        RuleFor(request => request.ReasonDetail)
            .NotEmpty()
            .When(request => request.Reason == RefundReason.Other)
            .WithMessage("Describe the reason for this refund.");

        RuleFor(request => request.ReasonDetail).MaximumLength(1000);
    }
}

/// <summary>Validator for approving a refund.</summary>
public sealed class DecideRefundRequestValidator : AbstractValidator<DecideRefundRequest>
{
    public DecideRefundRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Note).MaximumLength(1000);
    }
}

/// <summary>Validator for rejecting a refund.</summary>
public sealed class RejectRefundRequestValidator : AbstractValidator<RejectRefundRequest>
{
    public RejectRefundRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        // Mandatory: somebody asked for money back and is owed an answer.
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Explain why this refund is being rejected.")
            .MaximumLength(1000);
    }
}

/// <summary>Validator for assigning a chargeback.</summary>
public sealed class AssignChargebackRequestValidator : AbstractValidator<AssignChargebackRequest>
{
    public AssignChargebackRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.AssignToUserId)
            .NotEmpty().WithMessage("Choose who will work this case.");
    }
}

/// <summary>Validator for submitting chargeback evidence.</summary>
public sealed class SubmitChargebackEvidenceRequestValidator
    : AbstractValidator<SubmitChargebackEvidenceRequest>
{
    public SubmitChargebackEvidenceRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        // Evidence with no summary is not evidence. The bank reads the summary first, and an
        // empty one loses cases that could have been won.
        RuleFor(request => request.EvidenceSummary)
            .NotEmpty().WithMessage("Summarise the evidence being submitted.")
            .MaximumLength(4000);

        RuleFor(request => request.EvidenceDocumentUrls).MaximumLength(2000);
    }
}

/// <summary>Validator for resolving a chargeback.</summary>
public sealed class ResolveChargebackRequestValidator : AbstractValidator<ResolveChargebackRequest>
{
    public ResolveChargebackRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Outcome)
            .IsInEnum()
            .Must(outcome => outcome is ChargebackStatus.Won
                or ChargebackStatus.Lost
                or ChargebackStatus.Accepted)
            .WithMessage("A chargeback resolves as Won, Lost or Accepted.");

        RuleFor(request => request.ResolutionNote)
            .NotEmpty().WithMessage("Record what the bank decided and why.")
            .MaximumLength(2000);
    }
}
