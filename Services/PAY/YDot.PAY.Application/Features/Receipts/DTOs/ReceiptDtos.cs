using YDot.PAY.Application.Common.Models;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Receipts.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Issuing a receipt against a donation.
///
/// THE AMOUNT IS NOT A FIELD. A receipt is for what was actually given - the donation amount
/// less anything refunded - and letting a caller choose the figure on a tax document is exactly
/// the hole a receipt is supposed to close.
/// </summary>
public sealed record IssueReceiptRequest(
    /// <summary>The organisation's tax registration, as printed. From its IAM profile.</summary>
    string? OrganisationTaxReference = null,

    /// <summary>The exemption clause claimed, for example 80G in India.</summary>
    string? TaxExemptionReference = null,

    /// <summary>Whether to e-mail it straight away, or leave delivery for later.</summary>
    bool DeliverImmediately = true);

/// <summary>
/// Correcting an issued receipt.
///
/// A CORRECTION IS A NEW VERSION, never an edit. The original stays exactly as issued, because a
/// donor who claimed tax relief on version 1 must still be able to show what version 1 said.
/// </summary>
public sealed record CorrectReceiptRequest(
    long ExpectedVersion,
    string CorrectionReason,
    string? DonorName = null,
    string? DonorAddress = null,
    string? DonorTaxIdentifier = null,
    bool DeliverImmediately = true);

/// <summary>Voiding a receipt outright, where a correction is not enough.</summary>
public sealed record VoidReceiptRequest(long ExpectedVersion, string Reason);

/// <summary>Sending an issued receipt again, to the same address or another.</summary>
public sealed record ResendReceiptRequest(
    /// <summary>Email, Sms or Post. Defaults to e-mail.</summary>
    string Channel = "Email",

    /// <summary>
    /// Where to send it. Null uses the address on the receipt.
    ///
    /// AN OVERRIDE IS AUDITED, because sending a donor's tax document to a different address is
    /// exactly the action somebody would need to justify later.
    /// </summary>
    string? Destination = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>A receipt as a donation detail screen shows it inline.</summary>
public sealed record ReceiptSummaryResponse(
    Guid Id,
    string? ReceiptNumber,
    int VersionNumber,
    ReceiptStatus Status,
    string StatusDescription,
    ReceiptDeliveryStatus DeliveryStatus,
    MoneyResponse Amount,
    DateTimeOffset? IssuedAtUtc,
    string? DocumentUrl);

/// <summary>
/// One row of the receipt register - SCR-PAY-005.
///
/// The field names follow <c>receipt-register.model.ts</c> so the existing screen binds without
/// a translation layer.
/// </summary>
public sealed record ReceiptListItemResponse(
    Guid Id,
    string DonationReference,
    ReceiptStatus IssueState,
    string IssueStateDescription,
    ReceiptDeliveryStatus DeliveryState,
    string DeliveryStateDescription,
    string? ReceiptNumber,
    int VersionNumber,

    /// <summary>The donor as printed on the receipt, not as they are today.</summary>
    string DonorSnapshot,

    MoneyResponse Amount,
    string? CampaignOrFundName,
    DateTimeOffset? IssuedAtUtc,
    string FinancialYear,
    IReadOnlyList<ReceiptDeliveryResponse> DeliveryHistory,

    /// <summary>The receipt that superseded this one, or the one this superseded.</summary>
    Guid? SupersedesReceiptId,

    string? DocumentUrl,
    long Version);

/// <summary>The full receipt record.</summary>
public sealed record ReceiptDetailResponse(
    Guid Id,
    Guid TenantId,
    string? ReceiptNumber,
    int VersionNumber,
    Guid DonationId,
    string DonationReference,
    Guid? SupersedesReceiptId,
    string? SupersedesReceiptNumber,
    ReceiptStatus Status,
    string StatusDescription,
    ReceiptDeliveryStatus DeliveryStatus,
    string FinancialYear,
    MoneyResponse Amount,
    string DonorName,
    string DonorEmail,
    string? DonorAddress,
    string? DonorTaxIdentifier,
    string? CampaignOrFundName,
    string? OrganisationTaxReference,
    string? TaxExemptionReference,
    DateTimeOffset? IssuedAtUtc,
    Guid? IssuedByUserId,
    DateTimeOffset? VoidedAtUtc,
    Guid? VoidedByUserId,
    string? VoidReason,
    string? CorrectionReason,
    string? DocumentUrl,
    IReadOnlyList<ReceiptDeliveryResponse> Deliveries,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>One delivery attempt.</summary>
public sealed record ReceiptDeliveryResponse(
    Guid Id,
    string Channel,

    /// <summary>Masked unless the caller holds pay.donations.view-sensitive-donor.</summary>
    string Destination,

    ReceiptDeliveryStatus Status,
    string StatusDescription,
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string? FailureReason);

/// <summary>One line of the receipt export.</summary>
public sealed record ReceiptExportRow(
    string? ReceiptNumber,
    string VersionNumber,
    string DonationReference,
    string DonorName,
    string DonorEmail,
    string Amount,
    string Currency,
    string IssueState,
    string DeliveryState,
    string? IssuedAt,
    string FinancialYear,
    string? CampaignOrFund,
    string? TaxExemptionReference);

/// <summary>Filter for the receipt register.</summary>
public sealed class ReceiptSearchFilter : PaginationRequest
{
    public ReceiptStatus? IssueState { get; set; }

    public ReceiptDeliveryStatus? DeliveryState { get; set; }

    public string? FinancialYear { get; set; }

    public Guid? CampaignId { get; set; }

    public DateTimeOffset? IssuedFromUtc { get; set; }

    public DateTimeOffset? IssuedToUtc { get; set; }

    /// <summary>
    /// Receipts that were issued but never reached the donor.
    ///
    /// The queue somebody has to work: a donor entitled to a tax document who never received it
    /// will eventually ask, and it is better to find them first.
    /// </summary>
    public bool? UndeliveredOnly { get; set; }
}
