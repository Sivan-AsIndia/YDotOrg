using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Refunds.Mappings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Refunds.Commands.ManageChargeback;

/// <summary>The bank reversed a payment. Opens a case with a deadline.</summary>
public sealed record OpenChargebackCommand(
    string GatewayReference,
    string? GatewayDisputeReference,
    decimal DisputedAmount,
    string CurrencyCode,
    string? ReasonCode,
    string? ReasonDescription,
    DateTimeOffset? EvidenceDueAtUtc);

/// <summary>Gives the case an owner.</summary>
public sealed record AssignChargebackCommand(Guid ChargebackCaseId, AssignChargebackRequest Request);

/// <summary>Submits evidence to contest it.</summary>
public sealed record SubmitChargebackEvidenceCommand(
    Guid ChargebackCaseId, SubmitChargebackEvidenceRequest Request);

/// <summary>Records the bank's decision, or concedes.</summary>
public sealed record ResolveChargebackCommand(Guid ChargebackCaseId, ResolveChargebackRequest Request);

/// <summary>
/// Chargebacks: cases where a donor's bank reversed a payment without asking.
///
/// A CHARGEBACK IS NOT A REFUND, and the three differences drive everything here. The
/// organisation did not choose it. There is a DEADLINE to respond, and missing it loses the case
/// by default whatever the merits. And losing usually costs a fee on top of the money.
///
/// THE DEADLINE IS THE FIELD THE WHOLE CASE TURNS ON. It is stored, the queue sorts by it, and
/// the response carries the days remaining pre-computed - because a deadline somebody has to
/// work out for themselves is a deadline that gets missed.
///
/// THE DONATION IS MARKED ChargedBack IMMEDIATELY, before any decision. The money is already gone
/// from the account; treating the donation as intact until the case resolves would report income
/// the charity does not have.
/// </summary>
public sealed class ChargebackCommandHandler(
    IRefundRepository refunds,
    IDonationRepository donations,
    IReferenceGenerator references,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<PaymentSettings> paymentOptions,
    IUnitOfWork unitOfWork,
    ILogger<ChargebackCommandHandler> logger)
{
    private readonly PaymentSettings _settings = paymentOptions.Value;

    private const int ReferenceAttempts = 5;

    // =====================================================================================
    // Open
    // =====================================================================================

    /// <summary>
    /// Opens a chargeback case, normally from a gateway webhook.
    ///
    /// IT RESOLVES THE DONATION FROM THE GATEWAY REFERENCE, because a dispute arrives with no
    /// session and nothing else to identify the payment by.
    /// </summary>
    public async Task<Result<ChargebackCaseDetailResponse>> HandleAsync(
        OpenChargebackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var attempt = await donations.GetAttemptByGatewayReferenceAsync(
            command.GatewayReference, cancellationToken);

        if (attempt is null)
        {
            return Result.Failure<ChargebackCaseDetailResponse>(Error.NotFound(
                "No payment matches that gateway reference."));
        }

        var donation = await donations.GetDonationByIntentAsync(attempt.DonationIntentId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<ChargebackCaseDetailResponse>(Error.NotFound(
                "That payment has no recorded donation to charge back."));
        }

        // A gateway may notify the same dispute more than once. Returning the existing case makes
        // the redelivery a no-op rather than a second case with a second deadline.
        if (!string.IsNullOrWhiteSpace(command.GatewayDisputeReference))
        {
            var existing = await refunds.GetChargebackByDisputeReferenceAsync(
                command.GatewayDisputeReference, cancellationToken);

            if (existing is not null)
            {
                return existing.ToDetailResponse(
                    donation, clock.UtcNow, canSeeSensitiveDonor: true, PermittedActions(existing));
            }
        }

        var reference = await MintCaseReferenceAsync("CBK", cancellationToken);

        if (reference.IsFailure)
        {
            return Result.Failure<ChargebackCaseDetailResponse>(reference.Error!);
        }

        var now = clock.UtcNow;

        var chargeback = new ChargebackCase
        {
            TenantId = donation.TenantId,
            BusinessUnitId = donation.BusinessUnitId,
            CaseReference = reference.Value!,
            DonationId = donation.Id,
            Status = ChargebackStatus.Opened,
            GatewayDisputeReference = Clean(command.GatewayDisputeReference),
            ReasonCode = Clean(command.ReasonCode),
            ReasonDescription = Clean(command.ReasonDescription),
            DisputedAmount = MoneyValue.Create(command.DisputedAmount, command.CurrencyCode),
            OpenedAtUtc = now,

            // The gateway's deadline where it gave one, our default where it did not. A case with
            // no deadline is a case nobody prioritises.
            EvidenceDueAtUtc = command.EvidenceDueAtUtc
                               ?? now.AddDays(_settings.DefaultChargebackEvidenceDays)
        };

        await refunds.AddChargebackAsync(chargeback, cancellationToken);

        // MARKED IMMEDIATELY. The money is already gone from the account - see the class comment.
        donation.Status = DonationStatus.ChargedBack;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.ChargebackOpened,
            nameof(ChargebackCase),
            chargeback.Id,
            donation.TenantId,
            AuditResult.Succeeded,
            new
            {
                chargeback.CaseReference,
                donation.DonationReference,
                Amount = chargeback.DisputedAmount.ToString(),
                chargeback.EvidenceDueAtUtc
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Chargeback {CaseReference} opened against donation {DonationReference}. "
            + "Evidence is due by {EvidenceDue}.",
            chargeback.CaseReference, donation.DonationReference, chargeback.EvidenceDueAtUtc);

        return chargeback.ToDetailResponse(
            donation, now, canSeeSensitiveDonor: true, PermittedActions(chargeback));
    }

    // =====================================================================================
    // Assign
    // =====================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        AssignChargebackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var chargeback = await refunds.GetChargebackAsync(command.ChargebackCaseId, cancellationToken);

        if (chargeback is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That chargeback was not found."));
        }

        if (chargeback.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!chargeback.IsOpen)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A chargeback that is {chargeback.Status} cannot be reassigned."));
        }

        chargeback.AssignedToUserId = command.Request.AssignToUserId;

        // Assigning it moves it out of Opened: somebody is now working it, which is a different
        // state from nobody having looked.
        if (chargeback.Status == ChargebackStatus.Opened)
        {
            chargeback.Status = ChargebackStatus.EvidenceRequired;
        }

        await audit.WriteAsync(
            AuditActionCodes.ChargebackAssigned,
            nameof(ChargebackCase),
            chargeback.Id,
            new { chargeback.CaseReference, command.Request.AssignToUserId },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(chargeback, "Chargeback assigned.");
    }

    // =====================================================================================
    // Evidence
    // =====================================================================================

    /// <summary>
    /// Submits evidence.
    ///
    /// REFUSED AFTER THE DEADLINE, rather than accepted and silently ignored by the bank. Telling
    /// somebody their evidence went in when it cannot be considered is worse than telling them
    /// they missed it - the second at least lets them raise it internally.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        SubmitChargebackEvidenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var chargeback = await refunds.GetChargebackAsync(command.ChargebackCaseId, cancellationToken);

        if (chargeback is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That chargeback was not found."));
        }

        if (chargeback.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!chargeback.IsOpen)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A chargeback that is {chargeback.Status} is no longer accepting evidence."));
        }

        var now = clock.UtcNow;

        if (chargeback.EvidenceDueAtUtc.HasValue && chargeback.EvidenceDueAtUtc.Value < now)
        {
            return Result.Failure<OutcomeResponse>(Error.ChargebackDeadlinePassed(
                $"The evidence deadline for this chargeback passed on "
                + $"{chargeback.EvidenceDueAtUtc.Value:yyyy-MM-dd}."));
        }

        chargeback.EvidenceSummary = command.Request.EvidenceSummary.Trim();
        chargeback.EvidenceDocumentUrls = Clean(command.Request.EvidenceDocumentUrls);
        chargeback.EvidenceSubmittedAtUtc = now;
        chargeback.EvidenceSubmittedByUserId = currentUser.UserId;
        chargeback.Status = ChargebackStatus.UnderReview;

        await audit.WriteAsync(
            AuditActionCodes.ChargebackEvidenceSubmitted,
            nameof(ChargebackCase),
            chargeback.Id,
            new { chargeback.CaseReference, chargeback.EvidenceDueAtUtc },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(chargeback, "Evidence submitted. The case is now with the bank.");
    }

    // =====================================================================================
    // Resolve
    // =====================================================================================

    /// <summary>
    /// Records the outcome.
    ///
    /// WINNING RESTORES THE DONATION. The money comes back, so the donation stops being
    /// ChargedBack and returns to Recorded - and a charity's income figure should reflect that
    /// rather than staying permanently reduced by a dispute it won.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ResolveChargebackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var chargeback = await refunds.GetChargebackAsync(command.ChargebackCaseId, cancellationToken);

        if (chargeback is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That chargeback was not found."));
        }

        if (chargeback.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!chargeback.IsOpen)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"That chargeback is already {chargeback.Status}."));
        }

        if (command.Request.Outcome is not (ChargebackStatus.Won
            or ChargebackStatus.Lost
            or ChargebackStatus.Accepted))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "A chargeback resolves as Won, Lost or Accepted.",
                [new ValidationError(
                    nameof(command.Request.Outcome), "Choose Won, Lost or Accepted.")]));
        }

        var donation = await donations.GetDonationAsync(chargeback.DonationId, cancellationToken);

        chargeback.Status = command.Request.Outcome;
        chargeback.ResolvedAtUtc = clock.UtcNow;
        chargeback.ResolutionNote = command.Request.ResolutionNote.Trim();

        if (donation is not null)
        {
            donation.Status = command.Request.Outcome switch
            {
                // Won: the money is retained, so the donation is whole again.
                ChargebackStatus.Won => DonationStatus.Recorded,

                // Lost or conceded: the money is gone. It stays ChargedBack rather than becoming
                // Refunded, because the donor took it back rather than the charity giving it -
                // and a refund report should not include money that was seized.
                _ => DonationStatus.ChargedBack
            };
        }

        await audit.WriteAsync(
            AuditActionCodes.ChargebackResolved,
            nameof(ChargebackCase),
            chargeback.Id,
            new
            {
                chargeback.CaseReference,
                Outcome = command.Request.Outcome.ToString(),
                DonationStatus = donation?.Status.ToString()
            },
            command.Request.ResolutionNote,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = command.Request.Outcome switch
        {
            ChargebackStatus.Won => "Chargeback won. The donation has been restored.",
            ChargebackStatus.Lost => "Chargeback lost. The money has been reversed.",
            _ => "Chargeback conceded. The money has been reversed."
        };

        return BuildOutcome(chargeback, message);
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    private async Task<Result<string>> MintCaseReferenceAsync(
        string prefix, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReferenceAttempts; attempt++)
        {
            var candidate = references.NewCaseReference(prefix);

            if (!await refunds.CaseReferenceExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return Result.Failure<string>(Error.Dependency(
            "A unique case reference could not be generated."));
    }

    private OutcomeResponse BuildOutcome(ChargebackCase chargeback, string message) =>
        new(chargeback.Id,
            chargeback.Status.ToString(),
            chargeback.Version,
            message,
            PermittedActions(chargeback));

    private IReadOnlyList<string> PermittedActions(ChargebackCase chargeback) =>
        RefundMappingConfig.PermittedActionsFor(chargeback, clock.UtcNow, currentUser.HasPermission);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
