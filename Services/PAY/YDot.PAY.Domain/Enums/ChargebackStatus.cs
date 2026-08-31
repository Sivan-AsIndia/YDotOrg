namespace YDot.PAY.Domain.Enums;

/// <summary>
/// The lifecycle of a chargeback case.
///
/// A CHARGEBACK IS NOT A REFUND. The donor's bank reversed the payment without asking, there is
/// a deadline to respond, and losing costs a fee on top of the money. It is a separate case
/// type with its own evidence and its own clock.
/// </summary>
public enum ChargebackStatus
{
    /// <summary>The bank has reversed it. The clock is running.</summary>
    Opened = 0,

    /// <summary>Evidence is being assembled.</summary>
    EvidenceRequired = 1,

    /// <summary>Evidence submitted. Awaiting the bank.</summary>
    UnderReview = 2,

    /// <summary>Decided in the organisation's favour. The money comes back.</summary>
    Won = 3,

    /// <summary>Decided against. The money is gone, and usually a fee with it.</summary>
    Lost = 4,

    /// <summary>Conceded without contest, normally because the claim was valid.</summary>
    Accepted = 5
}
