using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// Money that actually arrived.
///
/// A DONATION EXISTS ONLY AFTER CAPTURE - section 24. Everything before that is an intent, and
/// a campaign's raised total counts these rows and never intents. That is the rule section 10
/// states as "the actual donor conversion happens after successful payment".
///
/// IT KEEPS ITS OWN COPY OF THE DONOR DETAILS rather than only pointing at the donor record.
/// A receipt is a tax document: it has to show the name and address as they stood ON THE DAY,
/// and a donor who later moves house must not silently rewrite the receipts already issued.
/// </summary>
public sealed class Donation : TenantEntity
{
    /// <summary>The public reference on the receipt and in every support conversation.</summary>
    public string DonationReference { get; set; } = string.Empty;

    public Guid DonationIntentId { get; set; }

    public DonationIntent DonationIntent { get; set; } = default!;

    /// <summary>The attempt that took the money. Which one matters when several were tried.</summary>
    public Guid PaymentAttemptId { get; set; }

    public PaymentAttempt PaymentAttempt { get; set; } = default!;

    /// <summary>The DON donor record. Set once the donor exists - see section 15.</summary>
    public Guid? DonorId { get; set; }

    public Guid? CampaignId { get; set; }

    // ---- The money -------------------------------------------------------------------

    /// <summary>What was captured. The figure on the receipt.</summary>
    public MoneyValue Amount { get; set; } = default!;

    /// <summary>
    /// What the gateway kept.
    ///
    /// Recorded because the organisation receives Amount minus this, and a finance team
    /// reconciling a bank statement needs both numbers to make the deposit add up.
    /// </summary>
    public MoneyValue? GatewayFee { get; set; }

    /// <summary>Amount minus fee: what actually lands in the bank.</summary>
    public MoneyValue? NetAmount { get; set; }

    /// <summary>How much has gone back so far. Zero for an untouched donation.</summary>
    public MoneyValue RefundedAmount { get; set; } = default!;

    public Guid? CurrencyId { get; set; }

    // ---- The donor, AS AT THE DONATION DATE ---------------------------------------------

    public string DonorName { get; set; } = string.Empty;

    public string DonorEmail { get; set; } = string.Empty;

    public string? DonorMobile { get; set; }

    public string? DonorTaxIdentifier { get; set; }

    /// <summary>The address as given at the time, flattened for the receipt.</summary>
    public string? DonorAddress { get; set; }

    // ---- Lifecycle ------------------------------------------------------------------------

    public DonationStatus Status { get; set; } = DonationStatus.Recorded;

    public DateTimeOffset DonatedAtUtc { get; set; }

    public PaymentMethodType? MethodType { get; set; }

    public string? GatewayReference { get; set; }

    public SettlementStatus SettlementStatus { get; set; } = SettlementStatus.Pending;

    public DateTimeOffset? SettledAtUtc { get; set; }

    /// <summary>The gateway's payout batch this donation was part of.</summary>
    public string? SettlementBatchReference { get; set; }

    public ReconciliationStatus ReconciliationStatus { get; set; } = ReconciliationStatus.Unreconciled;

    public DateTimeOffset? ReconciledAtUtc { get; set; }

    public string? ReconciliationNote { get; set; }

    // ---- Attribution, denormalised from the intent -------------------------------------------
    //
    // COPIED RATHER THAN JOINED, because donation reporting groups by these constantly - by
    // campaign, by source, by fundraiser - and every one of those reports would otherwise join
    // through the intent table for values that can never change once the money is in.

    public DonationSourceType SourceType { get; set; }

    public Guid? TrackingAssetId { get; set; }

    public Guid? LeadId { get; set; }

    public ICollection<Receipt> Receipts { get; set; } = [];

    public ICollection<RefundCase> RefundCases { get; set; } = [];

    public ICollection<ChargebackCase> ChargebackCases { get; set; } = [];

    // ---- Behaviour ---------------------------------------------------------------------------------

    /// <summary>What could still be given back.</summary>
    public MoneyValue RefundableAmount => Amount.Subtract(RefundedAmount);

    /// <summary>
    /// Whether a receipt may be issued.
    ///
    /// A VOIDED OR FULLY REFUNDED DONATION IS NOT RECEIPTABLE: a receipt is a tax document, and
    /// issuing one for money that went back would let a donor claim relief they are not entitled
    /// to.
    /// </summary>
    public bool IsReceiptable =>
        Status is DonationStatus.Recorded or DonationStatus.Settled or DonationStatus.PartiallyRefunded;

    /// <summary>True once a receipt has been issued and not since voided.</summary>
    public bool HasIssuedReceipt =>
        Receipts.Any(receipt => receipt.Status == ReceiptStatus.Issued);

    /// <summary>True while a refund or chargeback is still being worked.</summary>
    public bool HasOpenCase =>
        RefundCases.Any(refund => refund.IsOpen) || ChargebackCases.Any(chargeback => chargeback.IsOpen);
}
