using FluentValidation;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;

namespace YDots.CAM.Application.Features.BudgetTargetPlans.Validators;

/// <summary>Validator for allocating a plan.</summary>
public sealed class AllocateBudgetPlanRequestValidator : AbstractValidator<AllocateBudgetPlanRequest>
{
    public AllocateBudgetPlanRequestValidator()
    {
        RuleFor(request => request.CampaignId)
            .NotEmpty().WithMessage("Choose the campaign this plan belongs to.");

        RuleFor(request => request.PlanPeriod)
            .NotEmpty().WithMessage("Say which period the plan covers.")
            .MaximumLength(100);

        RuleFor(request => request.TargetDimension)
            .NotEmpty().WithMessage("Say what the target is measured along.")
            .MaximumLength(120);

        RuleFor(request => request.OwnerUserId)
            .NotEmpty().WithMessage("Name somebody accountable for this plan.");

        RuleFor(request => request.BudgetCategory)
            .NotEmpty().WithMessage("Choose a budget category.")
            .MaximumLength(120);

        // ZERO IS ALLOWED, NEGATIVE IS NOT. A plan being set up may legitimately have no figures
        // yet - submission is where they become mandatory - but a negative target or budget is not
        // a plan, it is a data-entry error that would quietly reduce a campaign's totals.
        RuleFor(request => request.TargetAmount)
            .GreaterThanOrEqualTo(0).WithMessage("A target cannot be negative.");

        RuleFor(request => request.BudgetAmount)
            .GreaterThanOrEqualTo(0).WithMessage("A budget cannot be negative.");

        RuleFor(request => request.ExpectedVolume)
            .GreaterThanOrEqualTo(0).WithMessage("An expected volume cannot be negative.");

        RuleFor(request => request.Assumptions).MaximumLength(4000);
    }
}

/// <summary>Validator for revising a plan into a new version.</summary>
public sealed class ReviseBudgetPlanRequestValidator : AbstractValidator<ReviseBudgetPlanRequest>
{
    public ReviseBudgetPlanRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.BudgetCategory)
            .NotEmpty().WithMessage("Choose a budget category.")
            .MaximumLength(120);

        RuleFor(request => request.TargetAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.BudgetAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ExpectedVolume).GreaterThanOrEqualTo(0);

        RuleFor(request => request.Assumptions).MaximumLength(4000);
        RuleFor(request => request.RevisionReason).MaximumLength(2000);
    }
}

/// <summary>Validator for editing a draft version.</summary>
public sealed class UpdateBudgetPlanVersionRequestValidator
    : AbstractValidator<UpdateBudgetPlanVersionRequest>
{
    public UpdateBudgetPlanVersionRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.BudgetCategory)
            .NotEmpty().WithMessage("Choose a budget category.")
            .MaximumLength(120);

        RuleFor(request => request.TargetAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.BudgetAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ExpectedVolume).GreaterThanOrEqualTo(0);

        RuleFor(request => request.Assumptions).MaximumLength(4000);
    }
}

/// <summary>Validator for submitting a version.</summary>
public sealed class SubmitBudgetPlanVersionRequestValidator
    : AbstractValidator<SubmitBudgetPlanVersionRequest>
{
    public SubmitBudgetPlanVersionRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Note).MaximumLength(2000);
    }
}

/// <summary>
/// Validator for approving or rejecting a version.
///
/// THE REASON IS NOT REQUIRED HERE. It is required on a rejection and optional on an approval, and
/// which of the two this is depends on the endpoint rather than on the payload - so the handler is
/// where that rule can actually be expressed.
/// </summary>
public sealed class BudgetPlanDecisionRequestValidator : AbstractValidator<BudgetPlanDecisionRequest>
{
    public BudgetPlanDecisionRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason).MaximumLength(2000);
    }
}
