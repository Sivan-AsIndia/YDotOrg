namespace YDot.PAY.Domain.Enums;

/// <summary>
/// The state of one attempt at a gateway.
///
/// SEPARATE FROM THE INTENT STATUS ON PURPOSE. One intent can have many attempts - a card
/// declined, then a different card, then success - and collapsing them into a single status
/// would lose exactly the history that Payment Support and Safe Retry exists to work from.
/// </summary>
public enum PaymentAttemptStatus
{
    /// <summary>Created locally; the donor has not been sent to the gateway yet.</summary>
    Initiated = 0,

    /// <summary>Handed to the gateway. Awaiting a result.</summary>
    Pending = 1,

    /// <summary>Authorised but not yet captured. Money is held, not taken.</summary>
    Authorised = 2,

    /// <summary>Captured. Money has moved.</summary>
    Succeeded = 3,

    /// <summary>The gateway refused it. The donor can try again.</summary>
    Failed = 4,

    /// <summary>The donor walked away from the gateway page.</summary>
    Abandoned = 5,

    /// <summary>
    /// No answer arrived within the expected window.
    ///
    /// DISTINCT FROM Failed, and the distinction is expensive to get wrong: a timeout means the
    /// outcome is UNKNOWN, so retrying may double-charge. It goes to the event queue for
    /// verification rather than being offered as a simple retry.
    /// </summary>
    TimedOut = 6
}
