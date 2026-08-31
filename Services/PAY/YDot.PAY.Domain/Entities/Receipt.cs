using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// A receipt issued against a donation.
///
/// A RECEIPT IS A TAX DOCUMENT, and that single fact drives the whole design here. It is never
/// edited in place: a mistake produces a NEW version that supersedes the old one, and the old
/// one stays exactly as issued. A donor who claimed tax relief on version 1 must still be able
/// to show what version 1 said.
///
/// THE NUMBER IS SEQUENTIAL PER ORGANISATION PER FINANCIAL YEAR, unlike almost every other
/// reference in the platform, and deliberately so: tax authorities expect receipt numbers to
/// run in an unbroken sequence, and a gap is something an auditor will ask about.
/// </summary>
public sealed class Receipt : TenantEntity
{
    /// <summary>
    /// The receipt number as printed. Unique inside the Organisation and its financial year.
    ///
    /// Null while the receipt is still a draft - a number is allocated when it is ISSUED, so an
    /// abandoned draft does not burn one and leave a gap in the sequence.
    /// </summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>1 for the original, 2 for the first correction. Shown beside the number.</summary>
    public int VersionNumber { get; set; } = 1;

    public Guid DonationId { get; set; }

    public Donation Donation { get; set; } = default!;

    /// <summary>The receipt this one supersedes. Null for an original.</summary>
    public Guid? SupersedesReceiptId { get; set; }

    public Receipt? Supersedes { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;

    public ReceiptDeliveryStatus DeliveryStatus { get; set; } = ReceiptDeliveryStatus.NotSent;

    /// <summary>The financial year the receipt falls in, as "2026-27". Part of the number's scope.</summary>
    public string FinancialYear { get; set; } = string.Empty;

    /// <summary>The amount receipted. Normally the donation amount, less any refund.</summary>
    public MoneyValue Amount { get; set; } = default!;

    // ---- The snapshot, AS AT ISSUE ------------------------------------------------------
    //
    // Copied from the donation, which copied it from the intent. Three copies sounds wasteful
    // until a donor changes their name and every historic receipt silently changes with it.

    public string DonorName { get; set; } = string.Empty;

    public string DonorEmail { get; set; } = string.Empty;

    public string? DonorAddress { get; set; }

    public string? DonorTaxIdentifier { get; set; }

    /// <summary>The campaign or fund the gift was for, as printed.</summary>
    public string? CampaignOrFundName { get; set; }

    /// <summary>The organisation's own tax registration, as printed on the receipt.</summary>
    public string? OrganisationTaxReference { get; set; }

    /// <summary>The tax exemption clause claimed, for example "80G" in India.</summary>
    public string? TaxExemptionReference { get; set; }

    public DateTimeOffset? IssuedAtUtc { get; set; }

    public Guid? IssuedByUserId { get; set; }

    public DateTimeOffset? VoidedAtUtc { get; set; }

    public Guid? VoidedByUserId { get; set; }

    public string? VoidReason { get; set; }

    /// <summary>Why this version exists, when it is a correction.</summary>
    public string? CorrectionReason { get; set; }

    /// <summary>Where the generated document lives. Null until it has been rendered.</summary>
    public string? DocumentUrl { get; set; }

    public ICollection<ReceiptDelivery> Deliveries { get; set; } = [];

    /// <summary>True once issued and not since voided or superseded.</summary>
    public bool IsValid => Status == ReceiptStatus.Issued;

    /// <summary>Whether this version may still be corrected.</summary>
    public bool CanBeCorrected => Status is ReceiptStatus.Issued;
}
