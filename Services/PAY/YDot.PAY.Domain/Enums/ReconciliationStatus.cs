namespace YDot.PAY.Domain.Enums;

/// <summary>Whether the gateway's record and ours agree.</summary>
public enum ReconciliationStatus
{
    /// <summary>Not yet compared.</summary>
    Unreconciled = 0,

    /// <summary>Compared and in agreement.</summary>
    Matched = 1,

    /// <summary>
    /// Compared and NOT in agreement - a different amount, a missing row on one side.
    ///
    /// The state that needs a person. It is deliberately not called Failed: nothing failed, the
    /// two records simply disagree and somebody has to decide which is right.
    /// </summary>
    Discrepancy = 2,

    /// <summary>A person looked at a discrepancy and resolved it.</summary>
    ManuallyResolved = 3
}
