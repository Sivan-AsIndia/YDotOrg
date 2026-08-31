using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Refunds.Mappings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Refunds.Commands.ManageRefund;

/// <summary>Raises a refund against a donation.</summary>
public sealed record RequestRefundCommand(Guid DonationId, RequestRefundRequest Request);

/// <summary>Approves a refund and submits it to the gateway.</summary>
public sealed record ApproveRefundCommand(Guid RefundCaseId, DecideRefundRequest Request);

/// <summary>Rejects a refund.</summary>
public sealed record RejectRefundCommand(Guid RefundCaseId, RejectRefundRequest Request);

/// <summary>
/// Refunds: raising, deciding and submitting them.
///
/// MONEY LEAVING THE ORGANISATION NEEDS TWO PEOPLE. Whoever raised the refund cannot approve it,
/// and that is enforced per record in <see cref="ApproveRefundCommand"/> rather than only by a
/// permission - a permission is held by a person, and this rule is about a person's relationship
/// to one particular case.
///
/// THE AMOUNT IS CHECKED AGAINST THE REFUNDABLE BALANCE, NOT THE DONATION. A donation already
/// partially refunded has less left than it was given, and refunding against the original amount
/// twice would take more back than ever came in.
///
/// A COMPLETED REFUND LEAVES THE RECEIPT WRONG until it is corrected. That is surfaced on the
/// case rather than silently fixed, because reissuing a tax document is a decision with its own
/// permission - but a refund with an uncorrected receipt leaves the donor holding relief they are
/// no longer entitled to, so the register has a filter for exactly that queue.
/// </summary>
public sealed class RefundCommandHandler(
    IRefundRepository refunds,
    IDonationRepository donations,
    IGatewayAccountRepository gatewayAccounts,
    IPaymentGateway paymentGateway,
    IReferenceGenerator references,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<RefundCommandHandler> logger)
{
    private const int ReferenceAttempts = 5;

    // =====================================================================================
    // Request
    // =====================================================================================

    public async Task<Result<RefundCaseDetailResponse>> HandleAsync(
        RequestRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var donation = await donations.GetDonationAsync(command.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<RefundCaseDetailResponse>(Error.NotFound("That donation was not found."));
        }

        if (donation.Status is DonationStatus.Voided)
        {
            return Result.Failure<RefundCaseDetailResponse>(Error.InvalidTransition(
                "A voided donation has nothing to refund."));
        }

        // A CHARGEBACK IS NOT A REFUND. The bank has already pulled the money back; refunding on
        // top would send it twice.
        if (donation.Status is DonationStatus.ChargedBack)
        {
            return Result.Failure<RefundCaseDetailResponse>(Error.InvalidTransition(
                "This donation is under a chargeback. The money has already been reversed."));
        }

        // Guards the double request: two refunds approved in parallel could between them exceed
        // the donation, and the gateway would refuse the second in a way nobody sees until
        // reconciliation.
        if (await refunds.HasOpenRefundAsync(donation.Id, cancellationToken))
        {
            return Result.Failure<RefundCaseDetailResponse>(Error.RefundAlreadyInProgress());
        }

        var amount = MoneyValue.Create(command.Request.Amount, donation.Amount.CurrencyCode);

        // CHECKED AGAINST WHAT IS LEFT, not against the original. See the class comment.
        if (amount.Amount > donation.RefundableAmount.Amount)
        {
            return Result.Failure<RefundCaseDetailResponse>(Error.RefundExceedsBalance(
                $"Only {donation.RefundableAmount} can still be refunded on this donation."));
        }

        var reference = await MintCaseReferenceAsync("REF", cancellationToken);

        if (reference.IsFailure)
        {
            return Result.Failure<RefundCaseDetailResponse>(reference.Error!);
        }

        var refundCase = new RefundCase
        {
            TenantId = donation.TenantId,
            BusinessUnitId = donation.BusinessUnitId,
            CaseReference = reference.Value!,
            DonationId = donation.Id,
            Status = RefundStatus.Requested,
            Reason = command.Request.Reason,
            ReasonDetail = Clean(command.Request.ReasonDetail),
            Amount = amount,
            RequestedByUserId = currentUser.UserId,
            RequestedAtUtc = clock.UtcNow
        };

        await refunds.AddRefundAsync(refundCase, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.RefundRequested,
            nameof(RefundCase),
            refundCase.Id,
            new
            {
                refundCase.CaseReference,
                donation.DonationReference,
                Amount = amount.ToString(),
                Reason = command.Request.Reason.ToString()
            },
            command.Request.ReasonDetail,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return refundCase.ToDetailResponse(
            donation, canSeeSensitiveDonor: true, PermittedActions(refundCase));
    }

    // =====================================================================================
    // Approve
    // =====================================================================================

    /// <summary>
    /// Approves a refund and submits it to the gateway.
    ///
    /// THE INDEPENDENCE CHECK COMES FIRST AND IS RECORDED AS A DENIED AUDIT ROW. An attempt to
    /// approve one's own refund request is exactly the pattern a later review looks for, so it
    /// leaves a trace rather than a silent 409.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApproveRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refundCase = await refunds.GetRefundAsync(command.RefundCaseId, cancellationToken);

        if (refundCase is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That refund was not found."));
        }

        if (refundCase.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (refundCase.Status != RefundStatus.Requested)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A refund that is {refundCase.Status} cannot be approved."));
        }

        if (!refundCase.CanBeDecidedBy(currentUser.UserId))
        {
            await audit.WriteAsync(
                AuditActionCodes.RefundApproved,
                nameof(RefundCase),
                refundCase.Id,
                AuditResult.Denied,
                new { refundCase.CaseReference },
                "Attempted to approve their own refund request.",
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot approve a refund you requested. Ask a colleague to review it."));
        }

        var donation = await donations.GetDonationAsync(refundCase.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Dependency(
                "That refund is not linked to a donation."));
        }

        // RE-CHECKED AT APPROVAL, not only at request. Another refund may have completed in
        // between, and the balance that was there when this was raised may be gone.
        if (refundCase.Amount.Amount > donation.RefundableAmount.Amount)
        {
            return Result.Failure<OutcomeResponse>(Error.RefundExceedsBalance(
                $"Only {donation.RefundableAmount} can still be refunded. "
                + "Another refund may have completed since this was raised."));
        }

        var now = clock.UtcNow;

        refundCase.Status = RefundStatus.Approved;
        refundCase.DecidedByUserId = currentUser.UserId;
        refundCase.DecidedAtUtc = now;
        refundCase.DecisionNote = Clean(command.Request.Note);

        await audit.WriteAsync(
            AuditActionCodes.RefundApproved,
            nameof(RefundCase),
            refundCase.Id,
            new { refundCase.CaseReference, donation.DonationReference },
            command.Request.Note,
            cancellationToken);

        // SAVED BEFORE THE GATEWAY CALL, so an approval survives a provider timeout and the case
        // can be resubmitted rather than lost.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var account = await gatewayAccounts.GetActiveForTenantAsync(donation.TenantId, cancellationToken);

        // OFFLINE IS DECIDED BY THE SOURCE, NOT BY WHETHER A REFERENCE STRING IS PRESENT.
        //
        // Offline entry stores the operator's own reference - a cheque number, a transfer
        // reference - in GatewayReference, because that is what reconciliation matches on. So the
        // "no reference means offline" test below was never true for a cheque, and approving its
        // refund sent a cheque number to Razorpay, which answered DEPENDENCY_FAILURE and left the
        // case stuck in Failed. A donation recorded offline was never charged through a provider,
        // so there is nothing there to reverse whatever reference it happens to carry.
        var wasNeverCharged = donation.SourceType == DonationSourceType.OfflineEntry;

        if (account is null || wasNeverCharged || string.IsNullOrWhiteSpace(donation.GatewayReference))
        {
            // An offline donation - a cheque, a bank transfer - has no gateway to refund through.
            // The approval stands and the money goes back by whatever means it came in.
            refundCase.Status = RefundStatus.Completed;
            refundCase.CompletedAtUtc = now;
            refundCase.DecisionNote =
                // Says WHY it is manual rather than assuming there was no reference at all. An
                // offline gift usually has one - a cheque number - it simply is not a gateway's.
                $"{refundCase.DecisionNote} ({(wasNeverCharged
                    ? "Recorded offline, so there is no gateway payment to reverse"
                    : "No gateway reference")}: refund to be made manually.)".Trim();

            ApplyRefundToDonation(donation, refundCase);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return BuildOutcome(
                refundCase, "Refund approved. It must be paid back manually - there is no gateway payment to reverse.");
        }

        GatewayRefundResult result;

        try
        {
            refundCase.Status = RefundStatus.Processing;
            refundCase.ProcessedAtUtc = now;

            result = await paymentGateway.RefundAsync(
                account,
                donation.GatewayReference,
                refundCase.Amount,
                references.NewIdempotencyKey(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The gateway could not be reached to refund case {CaseReference}.",
                refundCase.CaseReference);

            refundCase.Status = RefundStatus.Failed;
            refundCase.GatewayFailureReason = exception.Message;

            await audit.WriteAsync(
                AuditActionCodes.RefundFailed,
                nameof(RefundCase),
                refundCase.Id,
                AuditResult.Failed,
                new { refundCase.CaseReference },
                exception.Message,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.PaymentGatewayUnavailable(
                "The refund was approved but the provider could not be reached. It will be retried."));
        }

        if (!result.Accepted)
        {
            refundCase.Status = RefundStatus.Failed;
            refundCase.GatewayFailureReason = result.FailureMessage;

            await audit.WriteAsync(
                AuditActionCodes.RefundFailed,
                nameof(RefundCase),
                refundCase.Id,
                AuditResult.Failed,
                new { refundCase.CaseReference, result.FailureCode },
                result.FailureMessage,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.Dependency(
                $"The provider refused the refund: {result.FailureMessage}"));
        }

        refundCase.GatewayRefundReference = result.GatewayRefundReference;
        refundCase.Status = RefundStatus.Completed;
        refundCase.CompletedAtUtc = clock.UtcNow;

        ApplyRefundToDonation(donation, refundCase);

        await audit.WriteAsync(
            AuditActionCodes.RefundCompleted,
            nameof(RefundCase),
            refundCase.Id,
            new
            {
                refundCase.CaseReference,
                donation.DonationReference,
                Amount = refundCase.Amount.ToString(),
                NewDonationStatus = donation.Status.ToString()
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = donation.Status == DonationStatus.Refunded
            ? "Refund completed. The donation is now fully refunded - correct or void its receipt."
            : "Refund completed. The donation is partially refunded - correct its receipt for the new amount.";

        return BuildOutcome(refundCase, message);
    }

    // =====================================================================================
    // Reject
    // =====================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        RejectRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refundCase = await refunds.GetRefundAsync(command.RefundCaseId, cancellationToken);

        if (refundCase is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That refund was not found."));
        }

        if (refundCase.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (refundCase.Status != RefundStatus.Requested)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A refund that is {refundCase.Status} cannot be rejected."));
        }

        // The same independence rule as approval. Rejecting your own request is not as costly as
        // approving it, but it is the same failure of separation and the audit trail should not
        // have to distinguish them.
        if (!refundCase.CanBeDecidedBy(currentUser.UserId))
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot decide a refund you requested. Ask a colleague to review it."));
        }

        refundCase.Status = RefundStatus.Rejected;
        refundCase.DecidedByUserId = currentUser.UserId;
        refundCase.DecidedAtUtc = clock.UtcNow;
        refundCase.RejectionReason = command.Request.Reason.Trim();

        await audit.WriteAsync(
            AuditActionCodes.RefundRejected,
            nameof(RefundCase),
            refundCase.Id,
            new { refundCase.CaseReference },
            command.Request.Reason,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(refundCase, "Refund rejected.");
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    /// <summary>
    /// Moves the refunded total on the donation and re-derives its status.
    ///
    /// THE STATUS IS DERIVED, NEVER SET INDEPENDENTLY. Fully refunded means the refunded total
    /// equals the amount; anything less is partial. Deciding those separately is how a donation
    /// ends up marked Refunded with money still in it.
    /// </summary>
    private static void ApplyRefundToDonation(Donation donation, RefundCase refundCase)
    {
        donation.RefundedAmount = donation.RefundedAmount.Add(refundCase.Amount);

        donation.Status = donation.RefundedAmount.Amount >= donation.Amount.Amount
            ? DonationStatus.Refunded
            : DonationStatus.PartiallyRefunded;
    }

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

    private OutcomeResponse BuildOutcome(RefundCase refundCase, string message) =>
        new(refundCase.Id,
            refundCase.Status.ToString(),
            refundCase.Version,
            message,
            PermittedActions(refundCase));

    private IReadOnlyList<string> PermittedActions(RefundCase refundCase) =>
        RefundMappingConfig.PermittedActionsFor(refundCase, currentUser.UserId, currentUser.HasPermission);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
