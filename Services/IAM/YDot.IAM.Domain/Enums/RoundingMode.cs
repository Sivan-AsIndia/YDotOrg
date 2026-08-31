namespace YDot.IAM.Domain.Enums;

/// <summary>
/// How an amount in this currency is rounded to its smallest usable unit.
///
/// THIS IS A FINANCIAL DECISION, NOT A DISPLAY ONE. A receipt that rounds differently from
/// the payment gateway produces a reconciliation break, so the rule lives on the Currency row
/// where both sides can read the same value.
/// </summary>
public enum RoundingMode
{
    /// <summary>0.5 rounds away from zero. The common commercial default.</summary>
    HalfUp = 0,

    /// <summary>0.5 rounds towards zero.</summary>
    HalfDown = 1,

    /// <summary>0.5 rounds to the nearest even digit. Removes the upward bias over many rows.</summary>
    Bankers = 2
}
