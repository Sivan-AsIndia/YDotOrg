using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Donations.DTOs;

/// <summary>A donation as the intent detail screen shows it inline.</summary>
public sealed record DonationSummaryResponse(
    Guid Id,
    string DonationReference,
    MoneyResponse Amount,
    DonationStatus Status,
    string StatusDescription,
    DateTimeOffset DonatedAtUtc,
    bool HasIssuedReceipt,
    string? ReceiptNumber);

/// <summary>One row of the donation register.</summary>
public sealed record DonationListItemResponse(
    Guid Id,
    string DonationReference,
    string DonorName,

    /// <summary>Masked unless the caller holds pay.donations.view-sensitive-donor.</summary>
    string DonorEmail,

    MoneyResponse Amount,
    MoneyResponse? NetAmount,
    DonationStatus Status,
    string StatusDescription,
    SettlementStatus SettlementStatus,
    ReconciliationStatus ReconciliationStatus,
    DateTimeOffset DonatedAtUtc,
    PaymentMethodType? MethodType,
    Guid? CampaignId,
    string? CampaignName,
    DonationSourceType SourceType,
    bool HasIssuedReceipt,
    string? ReceiptNumber,
    bool HasOpenCase,
    long Version);

/// <summary>The full donation record.</summary>
public sealed record DonationDetailResponse(
    Guid Id,
    Guid TenantId,
    string DonationReference,
    Guid DonationIntentId,
    string IntentReference,
    Guid PaymentAttemptId,
    Guid? DonorId,
    Guid? CampaignId,
    string? CampaignName,
    MoneyResponse Amount,
    MoneyResponse? GatewayFee,
    MoneyResponse? NetAmount,
    MoneyResponse RefundedAmount,
    MoneyResponse RefundableAmount,
    string DonorName,
    string DonorEmail,
    string? DonorMobile,
    string? DonorTaxIdentifier,
    string? DonorAddress,
    DonationStatus Status,
    string StatusDescription,
    DateTimeOffset DonatedAtUtc,
    PaymentMethodType? MethodType,
    string? GatewayReference,
    SettlementStatus SettlementStatus,
    DateTimeOffset? SettledAtUtc,
    string? SettlementBatchReference,
    ReconciliationStatus ReconciliationStatus,
    DateTimeOffset? ReconciledAtUtc,
    string? ReconciliationNote,
    DonationSourceType SourceType,
    string SourceDescription,
    Guid? TrackingAssetId,
    Guid? LeadId,
    bool IsReceiptable,
    IReadOnlyList<ReceiptSummaryResponse> Receipts,
    IReadOnlyList<RefundCaseSummaryResponse> RefundCases,
    IReadOnlyList<ChargebackCaseSummaryResponse> ChargebackCases,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>
/// Recording a donation taken outside the gateway: a cheque, a bank transfer, cash.
///
/// IT STILL GOES THROUGH AN INTENT. Creating a donation with no intent would leave it with no
/// attribution and no consent record, and a cheque handed in at an event is just as much a gift
/// to a campaign as a card payment.
/// </summary>
public sealed record RecordOfflineDonationRequest(
    string DonorName,
    string Email,
    decimal Amount,
    string CurrencyCode,
    PaymentMethodType MethodType,
    DateTimeOffset ReceivedAtUtc,
    Guid? CampaignId = null,
    string? Mobile = null,
    string? TaxIdentifier = null,
    string? AddressLine1 = null,
    string? PostalCode = null,

    /// <summary>The cheque number or bank transfer reference. What reconciliation matches on.</summary>
    string? ExternalReference = null,

    string? Notes = null,
    bool ConsentGiven = true);

/// <summary>Marking a donation settled and reconciled against a bank statement.</summary>
public sealed record ReconcileDonationRequest(
    long ExpectedVersion,
    ReconciliationStatus Status,
    string? SettlementBatchReference = null,
    DateTimeOffset? SettledAtUtc = null,
    string? Note = null);

/// <summary>Counts and totals for the register's summary tiles.</summary>
public sealed record DonationStatisticsResponse(
    int TotalCount,
    MoneyResponse TotalAmount,
    MoneyResponse TotalRefunded,
    MoneyResponse NetAmount,
    int RecordedCount,
    int SettledCount,
    int RefundedCount,
    int ChargedBackCount,
    int AwaitingReceiptCount,
    int UnreconciledCount);

/// <summary>One line of the donation export.</summary>
public sealed record DonationExportRow(
    string DonationReference,
    string IntentReference,
    string DonorName,
    string DonorEmail,
    string Amount,
    string Currency,
    string NetAmount,
    string Status,
    string SettlementStatus,
    string ReconciliationStatus,
    string DonatedAt,
    string? Method,
    string? Campaign,
    string SourceType,
    string? ReceiptNumber,
    string RefundedAmount);

/// <summary>Filter for the donation register.</summary>
public sealed class DonationSearchFilter : PaginationRequest
{
    public DonationStatus? Status { get; set; }

    public SettlementStatus? SettlementStatus { get; set; }

    public ReconciliationStatus? ReconciliationStatus { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? DonorId { get; set; }

    public DonationSourceType? SourceType { get; set; }

    public PaymentMethodType? MethodType { get; set; }

    public DateTimeOffset? DonatedFromUtc { get; set; }

    public DateTimeOffset? DonatedToUtc { get; set; }

    public decimal? MinimumAmount { get; set; }

    public decimal? MaximumAmount { get; set; }

    /// <summary>Donations with no valid receipt. The queue the receipt register works from.</summary>
    public bool? AwaitingReceipt { get; set; }

    /// <summary>Donations with an open refund or chargeback case.</summary>
    public bool? HasOpenCase { get; set; }
}
