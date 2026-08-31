using FluentValidation;
using YDots.DON.Application.DTOs;

namespace YDots.DON.Application.Features.Donors.Validators;

/// <summary>
/// Validators for the three shared bodies in section 8. They live beside the Donor slice
/// because that is where they are first used, but the filter applies them to every controller
/// that accepts the same body.
/// </summary>
public sealed class TransitionRequestValidator : AbstractValidator<TransitionRequest>
{
    public TransitionRequestValidator()
    {
        RuleFor(request => request.Comment)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => !string.IsNullOrWhiteSpace(request.Comment));
    }
}

public sealed class DecisionRequestValidator : AbstractValidator<DecisionRequest>
{
    public DecisionRequestValidator()
    {
        // A rejection without a reason destroys the accountability the approval step exists for.
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Enter Decision reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.")
            .When(request => !request.Approved);

        RuleFor(request => request.Reason)
            .MaximumLength(2000).WithMessage("Use no more than 2,000 characters.")
            .When(request => request.Approved && !string.IsNullOrWhiteSpace(request.Reason));
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Enter Reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}
