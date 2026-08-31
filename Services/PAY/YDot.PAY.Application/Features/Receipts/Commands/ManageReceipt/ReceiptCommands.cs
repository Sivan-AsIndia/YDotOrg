using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Receipts.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Receipts.Commands.ManageReceipt;

/// <summary>Issues the first receipt for a donation.</summary>
public sealed record IssueReceiptCommand(Guid DonationId, IssueReceiptRequest Request);

/// <summary>Supersedes an issued receipt with a corrected version.</summary>
public sealed record CorrectReceiptCommand(Guid ReceiptId, CorrectReceiptRequest Request);

/// <summary>Voids a receipt outright.</summary>
public sealed record VoidReceiptCommand(Guid ReceiptId, VoidReceiptRequest Request);

/// <summary>Sends an issued receipt again.</summary>
public sealed record ResendReceiptCommand(Guid ReceiptId, ResendReceiptRequest Request);

/// <summary>
/// Receipts: issuing, correcting, voiding and delivering.
///
/// A RECEIPT IS A TAX DOCUMENT, and every rule here follows from that one fact.
///
/// IT IS NEVER EDITED IN PLACE. A mistake produces a NEW VERSION that supersedes the old one,
/// and the old one stays exactly as issued - a donor who claimed tax relief on version 1 must
/// still be able to show what version 1 said. That is why <see cref="CorrectReceiptCommand"/>
/// creates a row rather than updating one.
///
/// THE NUMBER IS SEQUENTIAL PER ORGANISATION PER FINANCIAL YEAR, unlike every other reference in
/// the platform. Tax authorities expect an unbroken series and a gap is something an auditor
/// asks about - so the number is allocated INSIDE the issuing transaction, from a counter that
/// is row-locked, and never from a random generator.
///
/// THE NUMBER IS ALLOCATED ON ISSUE, NOT ON CREATE. A draft that is abandoned would otherwise
/// burn a number and leave exactly the gap the sequence exists to avoid.
/// </summary>
public sealed class ReceiptCommandHandler(
    IReceiptRepository receipts,
    IDonationRepository donations,
    IReceiptDocumentService documents,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<PaymentSettings> paymentOptions,
    IUnitOfWork unitOfWork,
    ILogger<ReceiptCommandHandler> logger)
{
    private readonly PaymentSettings _settings = paymentOptions.Value;

    // =====================================================================================
    // Issue
    // =====================================================================================

    public async Task<Result<ReceiptDetailResponse>> HandleAsync(
        IssueReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var donation = await donations.GetDonationAsync(command.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.NotFound("That donation was not found."));
        }

        // A VOIDED OR FULLY REFUNDED DONATION IS NOT RECEIPTABLE. Issuing a receipt for money
        // that went back would let a donor claim relief they are not entitled to.
        if (!donation.IsReceiptable)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotEligible(
                $"A donation that is {donation.Status} cannot be receipted."));
        }

        var existing = await receipts.GetValidForDonationAsync(donation.Id, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptAlreadyIssued());
        }

        // The receiptable figure is what was actually GIVEN - the donation less anything already
        // refunded - not the original amount.
        var receiptableAmount = donation.RefundableAmount;

        if (receiptableAmount.IsZero)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotEligible(
                "This donation has been fully refunded, so there is nothing to receipt."));
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var financialYear = clock.FinancialYearFor(donation.DonatedAtUtc);

            // INSIDE THE TRANSACTION, and row-locked by the implementation. Two receipts issued
            // in the same instant must not take the same number.
            var sequence = await receipts.AllocateNextReceiptNumberAsync(
                donation.TenantId, financialYear, token);

            var receipt = new Receipt
            {
                TenantId = donation.TenantId,
                BusinessUnitId = donation.BusinessUnitId,
                DonationId = donation.Id,
                VersionNumber = 1,
                ReceiptNumber = FormatReceiptNumber(financialYear, sequence),
                Status = ReceiptStatus.Issued,
                DeliveryStatus = ReceiptDeliveryStatus.NotSent,
                FinancialYear = financialYear,
                Amount = receiptableAmount,

                // The donor AS AT THE DONATION, copied from the donation which copied it from the
                // intent. Three copies sounds wasteful until a donor changes their name and every
                // historic receipt silently changes with it.
                DonorName = donation.DonorName,
                DonorEmail = donation.DonorEmail,
                DonorAddress = donation.DonorAddress,
                DonorTaxIdentifier = donation.DonorTaxIdentifier,

                OrganisationTaxReference = Clean(command.Request.OrganisationTaxReference),
                TaxExemptionReference = Clean(command.Request.TaxExemptionReference),
                IssuedAtUtc = now,
                IssuedByUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId
            };

            await receipts.AddAsync(receipt, token);

            await audit.WriteAsync(
                AuditActionCodes.ReceiptIssued,
                nameof(Receipt),
                receipt.Id,
                new { receipt.ReceiptNumber, donation.DonationReference, Amount = receiptableAmount.ToString() },
                cancellationToken: token);

            await unitOfWork.SaveChangesAsync(token);

            // Rendering and delivery are best-effort: the receipt is validly issued the moment it
            // is numbered and recorded, and a PDF that would not render is a follow-up rather
            // than a reason to withhold a document the donor is entitled to.
            await RenderAndDeliverAsync(receipt, donation, command.Request.DeliverImmediately, token);

            return Result.Success(receipt.ToDetailResponse(
                donation.DonationReference,
                supersedesReceiptNumber: null,
                canSeeSensitiveDonor: true,
                PermittedActions(receipt)));
        }, cancellationToken);
    }

    // =====================================================================================
    // Correct
    // =====================================================================================

    /// <summary>
    /// Supersedes an issued receipt with a corrected version.
    ///
    /// THE ORIGINAL IS MARKED Corrected AND KEPT, never deleted or edited. The new version gets
    /// its own number from the same sequence, so both appear in the register and an auditor can
    /// see what changed and when.
    /// </summary>
    public async Task<Result<ReceiptDetailResponse>> HandleAsync(
        CorrectReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var original = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (original is null)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.NotFound("That receipt was not found."));
        }

        if (original.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.Concurrency());
        }

        if (!original.CanBeCorrected)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotCorrectable(
                $"A receipt that is {original.Status} cannot be corrected."));
        }

        var donation = await donations.GetDonationAsync(original.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<ReceiptDetailResponse>(Error.Dependency(
                "That receipt is not linked to a donation."));
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var financialYear = original.FinancialYear;

            var sequence = await receipts.AllocateNextReceiptNumberAsync(
                original.TenantId, financialYear, token);

            var corrected = new Receipt
            {
                TenantId = original.TenantId,
                BusinessUnitId = original.BusinessUnitId,
                DonationId = original.DonationId,

                // Version 2 supersedes version 1, and so on. The chain is what an auditor walks.
                VersionNumber = original.VersionNumber + 1,
                SupersedesReceiptId = original.Id,

                ReceiptNumber = FormatReceiptNumber(financialYear, sequence),
                Status = ReceiptStatus.Issued,
                DeliveryStatus = ReceiptDeliveryStatus.NotSent,
                FinancialYear = financialYear,

                // The AMOUNT IS RE-READ FROM THE DONATION rather than carried over, so a
                // correction issued after a partial refund shows what was actually kept.
                Amount = donation.RefundableAmount,

                DonorName = Clean(command.Request.DonorName) ?? original.DonorName,
                DonorEmail = original.DonorEmail,
                DonorAddress = Clean(command.Request.DonorAddress) ?? original.DonorAddress,
                DonorTaxIdentifier =
                    Clean(command.Request.DonorTaxIdentifier) ?? original.DonorTaxIdentifier,
                CampaignOrFundName = original.CampaignOrFundName,
                OrganisationTaxReference = original.OrganisationTaxReference,
                TaxExemptionReference = original.TaxExemptionReference,
                CorrectionReason = command.Request.CorrectionReason.Trim(),
                IssuedAtUtc = now,
                IssuedByUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId
            };

            await receipts.AddAsync(corrected, token);

            // The original is SUPERSEDED, not voided: it was validly issued and the donor may
            // have acted on it. Voided means "never valid", which is a different statement.
            original.Status = ReceiptStatus.Corrected;

            await audit.WriteAsync(
                AuditActionCodes.ReceiptCorrected,
                nameof(Receipt),
                corrected.Id,
                new
                {
                    corrected.ReceiptNumber,
                    SupersededNumber = original.ReceiptNumber,
                    donation.DonationReference
                },
                command.Request.CorrectionReason,
                token);

            await unitOfWork.SaveChangesAsync(token);

            await RenderAndDeliverAsync(corrected, donation, command.Request.DeliverImmediately, token);

            return Result.Success(corrected.ToDetailResponse(
                donation.DonationReference,
                original.ReceiptNumber,
                canSeeSensitiveDonor: true,
                PermittedActions(corrected)));
        }, cancellationToken);
    }

    // =====================================================================================
    // Void
    // =====================================================================================

    /// <summary>
    /// Voids a receipt.
    ///
    /// DIFFERENT FROM A CORRECTION. A correction says "this was right but the details have
    /// changed"; a void says "this should never have been issued". The number is NOT reused -
    /// the sequence keeps its gap-free property by keeping the voided row in it.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        VoidReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var receipt = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (receipt is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That receipt was not found."));
        }

        if (receipt.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (receipt.Status is ReceiptStatus.Voided)
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition("That receipt is already voided."));
        }

        receipt.Status = ReceiptStatus.Voided;
        receipt.VoidedAtUtc = clock.UtcNow;
        receipt.VoidedByUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId;
        receipt.VoidReason = command.Request.Reason.Trim();

        await audit.WriteAsync(
            AuditActionCodes.ReceiptVoided,
            nameof(Receipt),
            receipt.Id,
            new { receipt.ReceiptNumber },
            command.Request.Reason,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(receipt, "Receipt voided.");
    }

    // =====================================================================================
    // Resend
    // =====================================================================================

    /// <summary>
    /// Sends an issued receipt again.
    ///
    /// A DESTINATION OVERRIDE IS AUDITED WITH THE ADDRESS. Sending a donor's tax document
    /// somewhere other than the address on the receipt is exactly the action somebody would need
    /// to justify later.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ResendReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var receipt = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (receipt is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That receipt was not found."));
        }

        if (!receipt.IsValid)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A receipt that is {receipt.Status} cannot be sent."));
        }

        var destination = Clean(command.Request.Destination) ?? receipt.DonorEmail;
        var isOverride = !string.Equals(destination, receipt.DonorEmail, StringComparison.OrdinalIgnoreCase);

        var delivery = await DeliverAsync(receipt, command.Request.Channel, destination, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.ReceiptResent,
            nameof(Receipt),
            receipt.Id,
            new
            {
                receipt.ReceiptNumber,
                command.Request.Channel,
                DestinationOverridden = isOverride,

                // Recorded in full ONLY when it was overridden, because that is the case somebody
                // will need to review. The donor's own address is already on the receipt.
                Destination = isOverride ? destination : null,

                delivery.Succeeded
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(
            receipt,
            delivery.Succeeded
                ? "Receipt sent."
                : $"The receipt could not be sent: {delivery.FailureReason}");
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    /// <summary>
    /// Renders the document and, where asked, delivers it.
    ///
    /// EVERY FAILURE HERE IS LOGGED AND SWALLOWED. The receipt is validly issued the moment it is
    /// numbered and recorded; a PDF that would not render or an inbox that bounced is a follow-up
    /// task, not a reason to withhold a tax document the donor is entitled to.
    /// </summary>
    private async Task RenderAndDeliverAsync(
        Receipt receipt, Donation donation, bool deliver, CancellationToken cancellationToken)
    {
        try
        {
            var rendered = await documents.RenderAsync(receipt, cancellationToken);

            if (rendered.Succeeded)
            {
                receipt.DocumentUrl = rendered.DocumentUrl;
            }
            else
            {
                logger.LogWarning(
                    "Receipt {ReceiptNumber} could not be rendered: {Reason}. The receipt is still "
                    + "validly issued.", receipt.ReceiptNumber, rendered.FailureReason);
            }

            if (deliver && _settings.AutoDeliverReceipt)
            {
                await DeliverAsync(receipt, "Email", receipt.DonorEmail, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Rendering or delivering receipt {ReceiptNumber} failed. The receipt is still "
                + "validly issued and this needs following up.", receipt.ReceiptNumber);
        }
    }

    private async Task<ReceiptDeliveryResult> DeliverAsync(
        Receipt receipt, string channel, string destination, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var delivery = new ReceiptDelivery
        {
            TenantId = receipt.TenantId,
            BusinessUnitId = receipt.BusinessUnitId,
            ReceiptId = receipt.Id,
            Channel = channel,
            Destination = destination,
            Status = ReceiptDeliveryStatus.Pending,
            AttemptedAtUtc = now
        };

        ReceiptDeliveryResult result;

        try
        {
            result = await documents.DeliverAsync(receipt, channel, destination, cancellationToken);
        }
        catch (Exception exception)
        {
            result = new ReceiptDeliveryResult(false, null, exception.Message);
        }

        if (result.Succeeded)
        {
            delivery.Status = ReceiptDeliveryStatus.Delivered;
            delivery.DeliveredAtUtc = clock.UtcNow;
            delivery.ProviderReference = result.ProviderReference;

            receipt.DeliveryStatus = ReceiptDeliveryStatus.Delivered;
        }
        else
        {
            delivery.Status = ReceiptDeliveryStatus.Failed;
            delivery.FailureReason = result.FailureReason;

            // The RECEIPT's delivery status follows the attempt, so the register can show the
            // queue of donors who are entitled to a document that never reached them.
            receipt.DeliveryStatus = ReceiptDeliveryStatus.Failed;

            logger.LogWarning(
                "Receipt {ReceiptNumber} could not be delivered to {Channel}: {Reason}",
                receipt.ReceiptNumber, channel, result.FailureReason);
        }

        await receipts.AddDeliveryAsync(delivery, cancellationToken);

        await audit.WriteAsync(
            result.Succeeded
                ? AuditActionCodes.ReceiptDelivered
                : AuditActionCodes.ReceiptDeliveryFailed,
            nameof(Receipt),
            receipt.Id,
            result.Succeeded ? AuditResult.Succeeded : AuditResult.Failed,
            new { receipt.ReceiptNumber, channel, result.FailureReason },
            cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// "RCPT/2026-27/00042".
    ///
    /// The financial year is IN the number rather than only in a column, because that is how a
    /// receipt number is quoted on a tax return - and a number that needs a database lookup to
    /// tell you which year it belongs to is not much of a number.
    /// </summary>
    private string FormatReceiptNumber(string financialYear, int sequence) =>
        $"{_settings.ReceiptNumberPrefix}/{financialYear}/{sequence:00000}";

    private OutcomeResponse BuildOutcome(Receipt receipt, string message) =>
        new(receipt.Id,
            receipt.Status.ToString(),
            receipt.Version,
            message,
            PermittedActions(receipt));

    private IReadOnlyList<string> PermittedActions(Receipt receipt) =>
        ReceiptMappingConfig.PermittedActionsFor(receipt, currentUser.HasPermission);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
