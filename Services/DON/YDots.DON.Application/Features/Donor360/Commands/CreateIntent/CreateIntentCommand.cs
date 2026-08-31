using FluentValidation;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Features.Donor360.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.Donor360.Commands.CreateIntent;

/// <summary>
/// SCR-DON-003 Create intent. Records a stated giving intention as an open promise, which is
/// the draft the panel then shows.
/// </summary>
public sealed record CreateIntentCommand(Guid DonorId, CreateIntentRequest Request);

public sealed class CreateIntentCommandHandler(
    IDonorRepository donorRepository,
    IDonor360Repository donor360Repository,
    ICampaignRepository campaignRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    public async Task<Result<PromiseResponse>> HandleAsync(
        CreateIntentCommand command,
        CancellationToken cancellationToken = default)
    {
        var donor = await donorRepository.GetByIdAsync(command.DonorId, cancellationToken);

        if (donor is null || donor.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<PromiseResponse>(Error.DonorNotFound());
        }

        if (currentUser.Scope.IsOwnRecordsOnly && donor.RelationshipOwnerUserId != currentUser.UserId)
        {
            return Result.Failure<PromiseResponse>(Error.DonorNotFound());
        }

        if (donor.Status is DonorStatus.Archived or DonorStatus.Merged)
        {
            return Result.Failure<PromiseResponse>(Error.InvalidTransition(
                $"A donor in state {donor.Status} cannot record a new intent."));
        }

        if (donor.DoNotContact)
        {
            return Result.Failure<PromiseResponse>(Error.InvalidTransition(
                "This donor is marked Do not contact, so a new giving intent cannot be recorded."));
        }

        Campaign? campaign = null;

        if (command.Request.CampaignId is not null)
        {
            campaign = await campaignRepository.GetByIdAsync(command.Request.CampaignId.Value, cancellationToken);

            if (campaign is null || campaign.OrganisationId != currentUser.OrganisationId)
            {
                return Result.Failure<PromiseResponse>(Error.Validation(
                    "Review Campaign. Choose a campaign inside your scope.",
                    [new ValidationError(nameof(command.Request.CampaignId), "Choose a campaign from the list.")]));
            }
        }

        var now = clock.UtcNow;

        var promise = new DonorPromise
        {
            OrganisationId = donor.OrganisationId,
            DonorId = donor.Id,
            Reference = $"PRM-{now:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            Amount = command.Request.Amount,
            Currency = command.Request.Currency.Trim().ToUpperInvariant(),
            PromisedAtUtc = now,
            DueAtUtc = command.Request.DueAtUtc,
            Status = PromiseStatus.Open,
            CampaignId = campaign?.Id,
            Notes = command.Request.Notes.Trim()
        };

        donor360Repository.AddPromise(promise);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorIntentCreated, nameof(Donor), donor.Id, AuditResult.Succeeded,
                $"Intent {promise.Reference} recorded for {promise.Currency} {promise.Amount}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PromiseResponse(
            promise.Id,
            promise.Reference,
            promise.Amount,
            promise.Currency,
            promise.PromisedAtUtc,
            promise.DueAtUtc,
            promise.Status.ToString(),
            campaign?.Name));
    }
}

public sealed class CreateIntentRequestValidator : AbstractValidator<CreateIntentRequest>
{
    public CreateIntentRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThan(0).WithMessage("Review Amount. Enter a value greater than zero.");

        RuleFor(request => request.Currency)
            .NotEmpty().WithMessage("Enter Currency.")
            .Length(3).WithMessage("Use the three letter ISO code, for example INR.");

        RuleFor(request => request.Notes)
            .NotEmpty().WithMessage("Enter Notes.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}
