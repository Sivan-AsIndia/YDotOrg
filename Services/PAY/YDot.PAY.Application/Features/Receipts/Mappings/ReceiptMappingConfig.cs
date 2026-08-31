using System.Globalization;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Receipts.Mappings;

/// <summary>Manual mapping for the Receipts slice.</summary>
public static class ReceiptMappingConfig
{
    /// <summary>A receipt as a donation detail screen shows it inline.</summary>
    public static ReceiptSummaryResponse ToSummaryResponse(this Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptSummaryResponse(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.VersionNumber,
            receipt.Status,
            PaymentMappingConfig.Describe(receipt.Status),
            receipt.DeliveryStatus,
            receipt.Amount.ToResponse(),
            receipt.IssuedAtUtc,
            receipt.DocumentUrl);
    }

    /// <summary>One row of the receipt register.</summary>
    public static ReceiptListItemResponse ToListItemResponse(
        this Receipt receipt, string donationReference, bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptListItemResponse(
            receipt.Id,
            donationReference,
            receipt.Status,
            PaymentMappingConfig.Describe(receipt.Status),
            receipt.DeliveryStatus,
            PaymentMappingConfig.Describe(receipt.DeliveryStatus),
            receipt.ReceiptNumber,
            receipt.VersionNumber,

            // The donor as PRINTED on the receipt, not as they are today - and masked, because
            // the register is a staff grid rather than the donor's own copy.
            BuildDonorSnapshot(receipt, canSeeSensitiveDonor),

            receipt.Amount.ToResponse(),
            receipt.CampaignOrFundName,
            receipt.IssuedAtUtc,
            receipt.FinancialYear,
            [.. receipt.Deliveries.Select(delivery => delivery.ToResponse(canSeeSensitiveDonor))],
            receipt.SupersedesReceiptId,
            receipt.DocumentUrl,
            receipt.Version);
    }

    /// <summary>The full receipt record.</summary>
    public static ReceiptDetailResponse ToDetailResponse(
        this Receipt receipt,
        string donationReference,
        string? supersedesReceiptNumber,
        bool canSeeSensitiveDonor,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptDetailResponse(
            receipt.Id,
            receipt.TenantId,
            receipt.ReceiptNumber,
            receipt.VersionNumber,
            receipt.DonationId,
            donationReference,
            receipt.SupersedesReceiptId,
            supersedesReceiptNumber,
            receipt.Status,
            PaymentMappingConfig.Describe(receipt.Status),
            receipt.DeliveryStatus,
            receipt.FinancialYear,
            receipt.Amount.ToResponse(),
            receipt.DonorName,
            PaymentMappingConfig.MaskEmail(receipt.DonorEmail, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskAddress(receipt.DonorAddress, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskTaxIdentifier(receipt.DonorTaxIdentifier, canSeeSensitiveDonor),
            receipt.CampaignOrFundName,
            receipt.OrganisationTaxReference,
            receipt.TaxExemptionReference,
            receipt.IssuedAtUtc,
            receipt.IssuedByUserId,
            receipt.VoidedAtUtc,
            receipt.VoidedByUserId,
            receipt.VoidReason,
            receipt.CorrectionReason,
            receipt.DocumentUrl,
            [.. receipt.Deliveries
                .OrderByDescending(delivery => delivery.AttemptedAtUtc)
                .Select(delivery => delivery.ToResponse(canSeeSensitiveDonor))],
            receipt.CreatedAtUtc,
            receipt.CreatedByUserId,
            receipt.UpdatedAtUtc,
            receipt.UpdatedByUserId,
            receipt.Version,
            permittedActions);
    }

    /// <summary>One delivery attempt.</summary>
    public static ReceiptDeliveryResponse ToResponse(
        this ReceiptDelivery delivery, bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return new ReceiptDeliveryResponse(
            delivery.Id,
            delivery.Channel,

            // The destination is an e-mail address or a phone number, so it is masked exactly
            // like the donor's own contact details are.
            delivery.Channel.Equals("Email", StringComparison.OrdinalIgnoreCase)
                ? PaymentMappingConfig.MaskEmail(delivery.Destination, canSeeSensitiveDonor)
                : PaymentMappingConfig.MaskMobile(delivery.Destination, canSeeSensitiveDonor)
                  ?? string.Empty,

            delivery.Status,
            PaymentMappingConfig.Describe(delivery.Status),
            delivery.AttemptedAtUtc,
            delivery.DeliveredAtUtc,
            delivery.FailureReason);
    }

    /// <summary>One line of the receipt export.</summary>
    public static ReceiptExportRow ToExportRow(
        this Receipt receipt, string donationReference, bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptExportRow(
            receipt.ReceiptNumber,
            receipt.VersionNumber.ToString(CultureInfo.InvariantCulture),
            donationReference,
            receipt.DonorName,
            PaymentMappingConfig.MaskEmail(receipt.DonorEmail, canSeeSensitiveDonor),
            receipt.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            receipt.Amount.CurrencyCode,
            receipt.Status.ToString(),
            receipt.DeliveryStatus.ToString(),
            receipt.IssuedAtUtc?.ToString("u", CultureInfo.InvariantCulture),
            receipt.FinancialYear,
            receipt.CampaignOrFundName,
            receipt.TaxExemptionReference);
    }

    /// <summary>What may be done to this receipt next.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        Receipt receipt, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.ReceiptsView))
        {
            actions.Add("View");
        }

        if (hasPermission(PermissionCodes.ReceiptsExport))
        {
            actions.Add("Export");
        }

        // Only an ISSUED receipt can be acted on. A superseded or voided one is history: it is
        // read, and nothing else.
        if (receipt.Status != ReceiptStatus.Issued)
        {
            return actions;
        }

        if (hasPermission(PermissionCodes.ReceiptsCorrect))
        {
            actions.Add("Correct");
        }

        if (hasPermission(PermissionCodes.ReceiptsVoid))
        {
            actions.Add("Void");
        }

        if (hasPermission(PermissionCodes.ReceiptsResend))
        {
            actions.Add("Resend");
        }

        if (!string.IsNullOrWhiteSpace(receipt.DocumentUrl))
        {
            actions.Add("Download");
        }

        return actions;
    }

    /// <summary>The donor line the register shows: name plus a masked address.</summary>
    private static string BuildDonorSnapshot(Receipt receipt, bool canSeeSensitiveDonor) =>
        $"{receipt.DonorName} ({PaymentMappingConfig.MaskEmail(receipt.DonorEmail, canSeeSensitiveDonor)})";
}
