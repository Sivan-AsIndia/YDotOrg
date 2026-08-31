using System.Globalization;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Shared.Mappings;

/// <summary>
/// The mapping pieces every PAY slice shares: money rendering, status wording and donor masking.
///
/// MASKING LIVES HERE, IN ONE PLACE, AND THAT IS THE POINT. A donor's e-mail, mobile and tax
/// identifier appear on the intent register, the donation register, the receipt register, the
/// refund register, the chargeback queue and the support queue. Six implementations of "hide it
/// unless the caller may see it" is five chances to forget, and the one that forgets is the one
/// that leaks a tax identifier into a CSV.
/// </summary>
public static class PaymentMappingConfig
{
    /// <summary>
    /// Renders an amount for the client.
    ///
    /// The plain form is used where no currency master row is loaded. Where one is, the symbol
    /// and decimal places come from it - see <see cref="ToResponse(MoneyValue, string?, int)"/>.
    /// </summary>
    public static MoneyResponse ToResponse(this MoneyValue money)
    {
        ArgumentNullException.ThrowIfNull(money);

        return MoneyResponse.Plain(money.Amount, money.CurrencyCode);
    }

    /// <summary>Renders an amount with the currency's own symbol and decimal places.</summary>
    public static MoneyResponse ToResponse(this MoneyValue money, string? symbol, int decimalPlaces)
    {
        ArgumentNullException.ThrowIfNull(money);

        var places = Math.Clamp(decimalPlaces, 0, 8);
        var number = money.Amount.ToString(
            "N" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var display = string.IsNullOrWhiteSpace(symbol)
            ? $"{number} {money.CurrencyCode}"
            : $"{symbol}{number}";

        return new MoneyResponse(money.Amount, money.CurrencyCode, display);
    }

    /// <summary>Renders a nullable amount, or null.</summary>
    public static MoneyResponse? ToResponseOrNull(this MoneyValue? money) => money?.ToResponse();

    // =====================================================================================
    // Masking
    // =====================================================================================

    /// <summary>
    /// An e-mail address, masked unless the caller may see it.
    ///
    /// "jo***@example.org" keeps enough for a person to RECOGNISE an address they already know
    /// while not disclosing one they do not - which is exactly what a support agent confirming
    /// a donor's identity needs, and no more.
    /// </summary>
    public static string MaskEmail(string? email, bool canSeeSensitive)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        if (canSeeSensitive)
        {
            return email;
        }

        var atIndex = email.IndexOf('@', StringComparison.Ordinal);

        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        // A two-character local part cannot show two characters and still be masked, so it shows
        // one. A one-character one shows none.
        var visible = localPart.Length switch
        {
            <= 1 => string.Empty,
            2 => localPart[..1],
            _ => localPart[..2]
        };

        return $"{visible}***{domain}";
    }

    /// <summary>A mobile number, masked to its last three digits.</summary>
    public static string? MaskMobile(string? mobile, bool canSeeSensitive)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        if (canSeeSensitive)
        {
            return mobile;
        }

        return mobile.Length <= 3 ? "***" : $"******{mobile[^3..]}";
    }

    /// <summary>
    /// A tax identifier, masked to its last four characters.
    ///
    /// MASKED HARDER THAN AN E-MAIL, because a PAN or equivalent is an identity document number:
    /// it is reusable against the donor elsewhere in a way an e-mail address is not.
    /// </summary>
    public static string? MaskTaxIdentifier(string? taxIdentifier, bool canSeeSensitive)
    {
        if (string.IsNullOrWhiteSpace(taxIdentifier))
        {
            return null;
        }

        if (canSeeSensitive)
        {
            return taxIdentifier;
        }

        return taxIdentifier.Length <= 4 ? "****" : $"****{taxIdentifier[^4..]}";
    }

    /// <summary>An address, hidden entirely unless the caller may see it. There is no useful partial.</summary>
    public static string? MaskAddress(string? address, bool canSeeSensitive) =>
        string.IsNullOrWhiteSpace(address) ? null : canSeeSensitive ? address : "***";

    // =====================================================================================
    // Status wording
    // =====================================================================================

    public static string Describe(DonationIntentStatus status) => status switch
    {
        DonationIntentStatus.Draft => "Draft - not yet sent for payment",
        DonationIntentStatus.AwaitingPayment => "Awaiting payment",
        DonationIntentStatus.PaymentInProgress => "Payment in progress",
        DonationIntentStatus.Paid => "Paid",
        DonationIntentStatus.Failed => "Payment failed - can be retried",
        DonationIntentStatus.Expired => "Expired - the payment link lapsed",
        DonationIntentStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string Describe(PaymentAttemptStatus status) => status switch
    {
        PaymentAttemptStatus.Initiated => "Started",
        PaymentAttemptStatus.Pending => "Awaiting the payment provider",
        PaymentAttemptStatus.Authorised => "Authorised - not yet captured",
        PaymentAttemptStatus.Succeeded => "Succeeded",
        PaymentAttemptStatus.Failed => "Declined",
        PaymentAttemptStatus.Abandoned => "Abandoned by the donor",

        // Worded as UNKNOWN rather than failed. A timed-out attempt may well have charged the
        // donor, and describing it as a failure invites a second payment.
        PaymentAttemptStatus.TimedOut => "Outcome unknown - being verified with the provider",
        _ => status.ToString()
    };

    public static string Describe(DonationStatus status) => status switch
    {
        DonationStatus.Recorded => "Recorded",
        DonationStatus.Settled => "Settled to the bank",
        DonationStatus.PartiallyRefunded => "Partially refunded",
        DonationStatus.Refunded => "Fully refunded",
        DonationStatus.ChargedBack => "Charged back - under dispute",
        DonationStatus.Voided => "Voided",
        _ => status.ToString()
    };

    public static string Describe(ReceiptStatus status) => status switch
    {
        ReceiptStatus.Draft => "Draft",
        ReceiptStatus.Submitted => "Submitted",
        ReceiptStatus.PendingReview => "Pending review",
        ReceiptStatus.Issued => "Issued",
        ReceiptStatus.Corrected => "Superseded by a correction",
        ReceiptStatus.Voided => "Voided",
        _ => status.ToString()
    };

    public static string Describe(ReceiptDeliveryStatus status) => status switch
    {
        ReceiptDeliveryStatus.NotSent => "Not sent",
        ReceiptDeliveryStatus.Pending => "Sending",
        ReceiptDeliveryStatus.Delivered => "Delivered",
        ReceiptDeliveryStatus.Failed => "Delivery failed",
        _ => status.ToString()
    };

    public static string Describe(RefundStatus status) => status switch
    {
        RefundStatus.Requested => "Awaiting a decision",
        RefundStatus.Approved => "Approved - being sent to the provider",
        RefundStatus.Processing => "With the payment provider",
        RefundStatus.Completed => "Refunded",
        RefundStatus.Rejected => "Rejected",
        RefundStatus.Failed => "The provider could not process it",
        RefundStatus.Cancelled => "Withdrawn",
        _ => status.ToString()
    };

    public static string Describe(RefundReason reason) => reason switch
    {
        RefundReason.DonorRequested => "The donor asked for it back",
        RefundReason.DuplicateCharge => "Charged twice",
        RefundReason.IncorrectAmount => "Wrong amount taken",
        RefundReason.Fraudulent => "Fraudulent transaction",
        RefundReason.CampaignCancelled => "The campaign was cancelled",
        RefundReason.TestTransaction => "A test transaction",
        RefundReason.Other => "Other",
        _ => reason.ToString()
    };

    public static string Describe(ChargebackStatus status) => status switch
    {
        ChargebackStatus.Opened => "Opened - evidence needed",
        ChargebackStatus.EvidenceRequired => "Evidence being prepared",
        ChargebackStatus.UnderReview => "With the bank",
        ChargebackStatus.Won => "Won - the money is retained",
        ChargebackStatus.Lost => "Lost - the money is gone",
        ChargebackStatus.Accepted => "Conceded without contest",
        _ => status.ToString()
    };

    public static string Describe(PaymentEventStatus status) => status switch
    {
        PaymentEventStatus.Pending => "Awaiting processing",
        PaymentEventStatus.Processed => "Processed",
        PaymentEventStatus.Duplicate => "Duplicate - already applied",
        PaymentEventStatus.Failed => "Processing failed",
        PaymentEventStatus.Dismissed => "Dismissed by an operator",
        _ => status.ToString()
    };

    public static string Describe(PaymentEventType eventType) => eventType switch
    {
        PaymentEventType.PartiallyRefunded => "Partially refunded",
        PaymentEventType.ChargebackOpened => "Chargeback opened",
        PaymentEventType.ChargebackWon => "Chargeback won",
        PaymentEventType.ChargebackLost => "Chargeback lost",
        _ => eventType.ToString()
    };

    public static string Describe(DonationSourceType sourceType) => sourceType switch
    {
        DonationSourceType.FundraiserLead => "Fundraiser lead",
        DonationSourceType.QrCode => "QR code",
        DonationSourceType.Website => "Website",
        DonationSourceType.DirectLink => "Direct link",
        DonationSourceType.Email => "E-mail",
        DonationSourceType.Social => "Social media",
        DonationSourceType.CampaignLink => "Campaign link",
        DonationSourceType.OfflineEntry => "Recorded by staff",
        _ => sourceType.ToString()
    };
}
