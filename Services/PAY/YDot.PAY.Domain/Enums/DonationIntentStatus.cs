namespace YDot.PAY.Domain.Enums;

/// <summary>
/// The lifecycle of a donation intent, from section 24 of the module brief.
///
/// AN INTENT IS NOT A DONATION. It is a stated intention plus the attribution that explains
/// where it came from, and it stays an intent until money actually settles. That distinction is
/// the whole reason this enum has both <see cref="Paid"/> and a separate Donation record: a
/// campaign's raised total counts donations, never intents.
/// </summary>
public enum DonationIntentStatus
{
    /// <summary>Captured but not yet sent to a gateway. The state a saved-but-unpaid link is in.</summary>
    Draft = 0,

    /// <summary>A payment link exists and the donor has not acted on it yet.</summary>
    AwaitingPayment = 1,

    /// <summary>An attempt is in flight at the gateway.</summary>
    PaymentInProgress = 2,

    /// <summary>Money captured and a Donation recorded. The terminal success state.</summary>
    Paid = 3,

    /// <summary>The last attempt failed. Retryable - this is not terminal.</summary>
    Failed = 4,

    /// <summary>The payment link lapsed before anybody paid.</summary>
    Expired = 5,

    /// <summary>Abandoned deliberately, by the donor or by an operator.</summary>
    Cancelled = 6
}
