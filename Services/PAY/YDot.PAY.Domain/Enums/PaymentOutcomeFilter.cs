namespace YDot.PAY.Domain.Enums;

/// <summary>
/// What happened to the donor's money, as the Payment Queue asks it.
///
/// NOT THE SAME AS <see cref="PaymentEventStatus"/>, which is about whether we finished handling a
/// webhook. This is the question the workflow document asks - did the payment succeed, fail, or is
/// it still outstanding - and it is answered from the donation intent the event belongs to.
/// </summary>
public enum PaymentOutcomeFilter
{
    /// <summary>The gateway refused it. The queue offers Retry on these.</summary>
    Fail = 1,

    /// <summary>
    /// Still outstanding, which includes a donor who closed the payment window part-way.
    ///
    /// NEVER RETRIED AUTOMATICALLY. Pending can mean "the donor has already been charged and we
    /// have not heard yet", so the safe action is to verify with the gateway, not to charge again.
    /// </summary>
    Pending = 2,

    /// <summary>
    /// Paid. Present so the filter can express it, but the queue never shows these: the document
    /// says a success goes straight to the receipt and does not appear here at all.
    /// </summary>
    Success = 3,
}
