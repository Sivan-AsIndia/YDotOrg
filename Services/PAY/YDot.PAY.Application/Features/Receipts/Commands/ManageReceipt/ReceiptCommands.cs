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

        logger.LogInformation(
            "Starting receipt issuance for donation {DonationId}.",
            command.DonationId);

        var donation = await donations.GetDonationAsync(command.DonationId, cancellationToken);

        if (donation is null)
        {
            logger.LogWarning("Receipt issuance rejected because donation {DonationId} was not found.",
                command.DonationId);

            return Result.Failure<ReceiptDetailResponse>(
                Error.NotFound("That donation was not found."));
        }

        if (!donation.IsReceiptable)
        {
            logger.LogWarning("Receipt issuance rejected for donation {DonationId} because it is not receiptable.",
                command.DonationId);

            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotEligible(
                $"A donation that is {donation.Status} cannot be receipted."));
        }

        var existing = await receipts.GetValidForDonationAsync(donation.Id, cancellationToken);

        if (existing is not null)
        {
            logger.LogWarning("Receipt issuance rejected for donation {DonationId} because a valid receipt already exists.",
                command.DonationId);

            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptAlreadyIssued());
        }

        var receiptableAmount = donation.RefundableAmount;

        if (receiptableAmount.IsZero)
        {
            logger.LogWarning("Receipt issuance rejected for donation {DonationId} because the receiptable amount is zero.",
                command.DonationId);

            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotEligible(
                "This donation has been fully refunded, so there is nothing to receipt."));
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var financialYear = clock.FinancialYearFor(donation.DonatedAtUtc);

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

            await RenderAndDeliverAsync(receipt, donation, command.Request.DeliverImmediately, token);

            logger.LogInformation("Receipt {ReceiptNumber} issued successfully for donation {DonationReference}.",
                receipt.ReceiptNumber,donation.DonationReference);

            return Result.Success(receipt.ToDetailResponse(
                donation.DonationReference,
                supersedesReceiptNumber: null,
                canSeeSensitiveDonor: true,
                PermittedActions(receipt)));
        }, cancellationToken);

        return result;
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

        logger.LogInformation("Starting correction of receipt {ReceiptId}.",
            command.ReceiptId);

        var original = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (original is null)
        {
            logger.LogWarning("Receipt correction rejected because receipt {ReceiptId} was not found.",
                command.ReceiptId);

            return Result.Failure<ReceiptDetailResponse>(
                Error.NotFound("That receipt was not found."));
        }

        if (original.Version != command.Request.ExpectedVersion)
        {
            logger.LogWarning("Receipt correction rejected for receipt {ReceiptId} because the record version is stale.",
                command.ReceiptId);

            return Result.Failure<ReceiptDetailResponse>(Error.Concurrency());
        }

        if (!original.CanBeCorrected)
        {
            logger.LogWarning("Receipt correction rejected for receipt {ReceiptId} because its status is {Status}.",
                command.ReceiptId,original.Status);

            return Result.Failure<ReceiptDetailResponse>(Error.ReceiptNotCorrectable(
                $"A receipt that is {original.Status} cannot be corrected."));
        }

        var donation = await donations.GetDonationAsync(original.DonationId, cancellationToken);

        if (donation is null)
        {
            logger.LogWarning("Receipt correction rejected for receipt {ReceiptId} because its linked donation was not found.",
                command.ReceiptId);

            return Result.Failure<ReceiptDetailResponse>(Error.Dependency(
                "That receipt is not linked to a donation."));
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
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
                VersionNumber = original.VersionNumber + 1,
                SupersedesReceiptId = original.Id,
                ReceiptNumber = FormatReceiptNumber(financialYear, sequence),
                Status = ReceiptStatus.Issued,
                DeliveryStatus = ReceiptDeliveryStatus.NotSent,
                FinancialYear = financialYear,
                Amount = donation.RefundableAmount,
                DonorName = Clean(command.Request.DonorName) ?? original.DonorName,
                DonorEmail = original.DonorEmail,
                DonorAddress = Clean(command.Request.DonorAddress) ?? original.DonorAddress,
                DonorTaxIdentifier = Clean(command.Request.DonorTaxIdentifier) ?? original.DonorTaxIdentifier,
                CampaignOrFundName = original.CampaignOrFundName,
                OrganisationTaxReference = original.OrganisationTaxReference,
                TaxExemptionReference = original.TaxExemptionReference,
                CorrectionReason = command.Request.CorrectionReason.Trim(),
                IssuedAtUtc = now,
                IssuedByUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId
            };

            await receipts.AddAsync(corrected, token);

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

            logger.LogInformation("Receipt {ReceiptNumber} corrected successfully. Original receipt {OriginalReceiptId} was superseded.",
                corrected.ReceiptNumber,original.Id);

            return Result.Success(corrected.ToDetailResponse(
                donation.DonationReference,
                original.ReceiptNumber,
                canSeeSensitiveDonor: true,
                PermittedActions(corrected)));
        }, cancellationToken);

        return result;
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

        logger.LogInformation("Starting void operation for receipt {ReceiptId}.",
            command.ReceiptId);

        var receipt = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (receipt is null)
        {
            logger.LogWarning("Receipt void operation rejected because receipt {ReceiptId} was not found.",
                command.ReceiptId);

            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That receipt was not found."));
        }

        if (receipt.Version != command.Request.ExpectedVersion)
        {
            logger.LogWarning("Receipt void operation rejected for receipt {ReceiptId} because the record version is stale.",
                command.ReceiptId);

            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (receipt.Status is ReceiptStatus.Voided)
        {
            logger.LogWarning("Receipt void operation rejected for receipt {ReceiptId} because it is already voided.",
                command.ReceiptId);

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

        logger.LogInformation("Receipt {ReceiptNumber} voided successfully.",
            receipt.ReceiptNumber);

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

        logger.LogInformation("Starting resend operation for receipt {ReceiptId}.",
            command.ReceiptId);

        var receipt = await receipts.GetAsync(command.ReceiptId, cancellationToken);

        if (receipt is null)
        {
            logger.LogWarning("Receipt resend rejected because receipt {ReceiptId} was not found.",
                command.ReceiptId);

            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That receipt was not found."));
        }

        if (!receipt.IsValid)
        {
            logger.LogWarning("Receipt resend rejected for receipt {ReceiptId} because its status is {Status}.",
                command.ReceiptId,receipt.Status);

            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A receipt that is {receipt.Status} cannot be sent."));
        }

        var destination = Clean(command.Request.Destination) ?? receipt.DonorEmail;
        var isOverride = !string.Equals(destination, receipt.DonorEmail, StringComparison.OrdinalIgnoreCase);

        if (isOverride)
        {
            logger.LogWarning("Receipt {ReceiptNumber} is being resent using an overridden delivery destination.",
                receipt.ReceiptNumber);
        }

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
                Destination = isOverride ? destination : null,
                delivery.Succeeded
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (delivery.Succeeded)
        {
            logger.LogInformation("Receipt {ReceiptNumber} resent successfully.",
                receipt.ReceiptNumber);
        }
        else
        {
            logger.LogWarning("Receipt {ReceiptNumber} resend failed.",
                receipt.ReceiptNumber);
        }

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

                logger.LogInformation("Receipt {ReceiptNumber} document rendered successfully.",
                    receipt.ReceiptNumber);
            }
            else
            {
                logger.LogWarning("Receipt {ReceiptNumber} could not be rendered: {Reason}. The receipt is still " + "validly issued.",
                    receipt.ReceiptNumber,rendered.FailureReason);
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
                + "validly issued and this needs following up.",
                receipt.ReceiptNumber);
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
            logger.LogError(exception,
                "Receipt {ReceiptNumber} delivery through {Channel} encountered an exception.",
                receipt.ReceiptNumber,channel);

            result = new ReceiptDeliveryResult(false, null, exception.Message);
        }

        if (result.Succeeded)
        {
            delivery.Status = ReceiptDeliveryStatus.Delivered;
            delivery.DeliveredAtUtc = clock.UtcNow;
            delivery.ProviderReference = result.ProviderReference;
            receipt.DeliveryStatus = ReceiptDeliveryStatus.Delivered;

            logger.LogInformation("Receipt {ReceiptNumber} delivered successfully through {Channel}.",
                receipt.ReceiptNumber,channel);
        }
        else
        {
            delivery.Status = ReceiptDeliveryStatus.Failed;
            delivery.FailureReason = result.FailureReason;
            receipt.DeliveryStatus = ReceiptDeliveryStatus.Failed;

            logger.LogWarning("Receipt {ReceiptNumber} could not be delivered through {Channel}: {Reason}.",
                receipt.ReceiptNumber,channel,result.FailureReason);
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
        new(
            receipt.Id,
            receipt.Status.ToString(),
            receipt.Version,
            message,
            PermittedActions(receipt));

    private IReadOnlyList<string> PermittedActions(Receipt receipt) =>
        ReceiptMappingConfig.PermittedActionsFor(receipt, currentUser.HasPermission);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}