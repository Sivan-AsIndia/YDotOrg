namespace YDot.PAY.Domain.Enums;

/// <summary>
/// Whether the money has reached the organisation's bank account.
///
/// SEPARATE FROM CAPTURE, and the gap between them is where reconciliation lives: a gateway
/// captures immediately and settles days later, minus its fee. A donation is real from capture,
/// but it is not in the bank until settlement.
/// </summary>
public enum SettlementStatus
{
    Pending = 0,
    Settled = 1,

    /// <summary>The gateway held it back - a risk review, a KYC gap.</summary>
    OnHold = 2,

    /// <summary>Reversed after settlement, normally a chargeback.</summary>
    Reversed = 3
}
