namespace YDot.PAY.Domain.Enums;

/// <summary>
/// The state of a recorded donation.
///
/// A donation exists only once money has actually been captured, so there is no Pending here -
/// that state belongs to the intent. What varies afterwards is whether the money stayed.
/// </summary>
public enum DonationStatus
{
    /// <summary>Money captured and recorded. The normal state.</summary>
    Recorded = 0,

    /// <summary>Money reached the organisation's bank account.</summary>
    Settled = 1,

    /// <summary>Some of it has been given back.</summary>
    PartiallyRefunded = 2,

    /// <summary>All of it has been given back.</summary>
    Refunded = 3,

    /// <summary>The donor's bank reversed it. Under dispute.</summary>
    ChargedBack = 4,

    /// <summary>
    /// Recorded in error and reversed in the books.
    ///
    /// DISTINCT FROM Refunded: a refund means money went back to the donor, while a void means
    /// the donation should never have been recorded. The receipt handling differs, which is why
    /// the two are not one state.
    /// </summary>
    Voided = 5
}
