using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// The central record of the whole payment flow: somebody intends to give, and this is
/// everything known about that intention before any money has moved.
///
/// SECTION 22 OF THE MODULE BRIEF IS WHY THIS TYPE EXISTS. Nine entry channels - a fundraiser's
/// lead link, a QR code, a website button, an e-mail, a social post - all converge on ONE
/// payment decision. The intent is that convergence point: whatever the channel, it produces an
/// intent, and everything downstream reads the intent rather than knowing about the channel.
///
/// IT IS NOT A DONATION. A campaign's raised total counts <see cref="Donation"/> rows, never
/// intents - section 10: "the donation is not treated as a completed donation merely because
/// the Lead is qualified". An intent becomes a donation only when money is actually captured.
///
/// THE ATTRIBUTION FIELDS ARE THE POINT OF SECTION 27. Without SourceType, CampaignId,
/// TrackingAssetId and LeadId on this row, the question "where did this donation come from?"
/// has no answer once the money is in - and neither does "which fundraiser captured the lead
/// that produced it?".
/// </summary>
public class DonationIntent : TenantEntity
{
    /// <summary>
    /// The public, unguessable reference the donor and support both quote.
    ///
    /// It appears in the payment link, in the thank-you page and in every support conversation,
    /// so it is generated from random bytes rather than a sequence: a sequential reference lets
    /// anybody holding one enumerate every other donation on the platform.
    /// </summary>
    public string IntentReference { get; set; } = string.Empty;

    // ---- Attribution: section 27 -----------------------------------------------------

    /// <summary>How the donor arrived. The single most important reporting dimension here.</summary>
    public DonationSourceType SourceType { get; set; }

    /// <summary>The campaign being given to. Null for an untargeted general donation.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>
    /// The CAM tracking asset that produced this intent - the QR code, the short link.
    ///
    /// Not a foreign key: CAM and PAY are separately deployable services that share a database,
    /// and a cross-service FK would let a schema change in one block a migration in the other.
    /// </summary>
    public Guid? TrackingAssetId { get; set; }

    /// <summary>The tracking reference carried back from the link. Resolves to the asset above.</summary>
    public string? TrackingReference { get; set; }

    /// <summary>
    /// The DON lead this intent came from, where a fundraiser captured the donor first.
    ///
    /// Section 28: the lead history must not be lost. This is the link that lets a report say
    /// which fundraiser captured the lead and which lead owner converted it.
    /// </summary>
    public Guid? LeadId { get; set; }

    /// <summary>The existing donor, once the organisation-scoped check has matched one.</summary>
    public Guid? DonorId { get; set; }

    // ---- What the donor told us ---------------------------------------------------------

    public string DonorName { get; set; } = string.Empty;

    /// <summary>
    /// Stored as given, for display and receipts.
    ///
    /// The MATCHING is done on <see cref="NormalisedEmail"/> instead - see section 26, where the
    /// existing-donor check is organisation plus normalised e-mail.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Lower-cased and trimmed. The column the existing-donor lookup and its index use.
    ///
    /// A SEPARATE COLUMN RATHER THAN A FUNCTION INDEX, because the same normalisation has to
    /// apply on the way in and on the way out. Matching "John@Gmail.com" against "john@gmail.com"
    /// by lowering at query time works until one query forgets.
    /// </summary>
    public string NormalisedEmail { get; set; } = string.Empty;

    public string? Mobile { get; set; }

    /// <summary>Tax identifier, where the jurisdiction needs one on the receipt. PAN in India.</summary>
    public string? TaxIdentifier { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    /// <summary>Rows in the IAM geography master. Not FKs - see the TrackingAssetId note.</summary>
    public Guid? CountryId { get; set; }

    public Guid? StateId { get; set; }

    public Guid? CityId { get; set; }

    public string? PostalCode { get; set; }

    // ---- The money ---------------------------------------------------------------------------

    /// <summary>What the donor intends to give. Owned type: amount and currency together.</summary>
    public MoneyValue Amount { get; set; } = default!;

    /// <summary>The IAM currency master row matching Amount.CurrencyCode. Carried for reporting joins.</summary>
    public Guid? CurrencyId { get; set; }

    // ---- Consent and preferences --------------------------------------------------------------

    /// <summary>
    /// Whether the donor agreed to the terms shown at capture.
    ///
    /// Stored with the TEXT VERSION they agreed to, because "did they consent?" is not a useful
    /// answer three years later without "to what?".
    /// </summary>
    public bool ConsentGiven { get; set; }

    public string? ConsentVersion { get; set; }

    public DateTimeOffset? ConsentGivenAtUtc { get; set; }

    /// <summary>Whether the donor is happy to be named publicly. Defaults to no.</summary>
    public bool AllowPublicRecognition { get; set; }

    /// <summary>The name to show where recognition is allowed, when it differs from the legal one.</summary>
    public string? PublicRecognitionName { get; set; }

    // ---- Lifecycle ----------------------------------------------------------------------------------

    public DonationIntentStatus Status { get; set; } = DonationIntentStatus.Draft;

    /// <summary>Where the donor is sent to pay. Null until a payment link is created.</summary>
    public string? PaymentLinkUrl { get; set; }

    /// <summary>
    /// When the payment link stops working.
    ///
    /// A link that never expires is a link that can be replayed months later against a campaign
    /// that has since closed.
    /// </summary>
    public DateTimeOffset? PaymentLinkExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether an existing donor was found for this organisation and e-mail.
    ///
    /// Null means the check has not run yet, which is different from "no donor found". Sections
    /// 13 and 14 branch on this: an existing donor is sent to sign in, a new one continues
    /// straight to payment.
    /// </summary>
    public bool? ExistingDonorMatched { get; set; }

    public DateTimeOffset? ExistingDonorCheckedAtUtc { get; set; }

    /// <summary>How many times the donor has been sent to a gateway. Drives the support view.</summary>
    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public string? FailureReason { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<PaymentAttempt> Attempts { get; set; } = [];

    /// <summary>
    /// The donation, once one exists.
    ///
    /// AT MOST ONE, which is the invariant that stops a double-charge becoming double income:
    /// two successful captures against one intent produce one donation and a refund case, never
    /// two donations.
    /// </summary>
    public Donation? Donation { get; set; }

    // ---- Behaviour ------------------------------------------------------------------------------------

    /// <summary>True while the intent can still be paid.</summary>
    public bool IsPayable => Status is DonationIntentStatus.Draft
        or DonationIntentStatus.AwaitingPayment
        or DonationIntentStatus.Failed;

    /// <summary>True once it has reached a state nothing moves it out of.</summary>
    public bool IsTerminal => Status is DonationIntentStatus.Paid
        or DonationIntentStatus.Expired
        or DonationIntentStatus.Cancelled;

    /// <summary>Whether the payment link has lapsed as at the given moment.</summary>
    public bool IsLinkExpiredAt(DateTimeOffset moment) =>
        PaymentLinkExpiresAtUtc.HasValue && PaymentLinkExpiresAtUtc.Value <= moment;

    /// <summary>
    /// Whether this intent came from a fundraiser-captured lead.
    ///
    /// Section 16: a successful payment on one of these converts the lead to a donor, and the
    /// conversion has to be reported back to DON.
    /// </summary>
    public bool OriginatedFromLead => LeadId.HasValue;
}
