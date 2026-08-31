namespace YDot.PAY.Domain.Enums;

/// <summary>The lifecycle of a refund request.</summary>
public enum RefundStatus
{
    /// <summary>Raised, awaiting a decision.</summary>
    Requested = 0,

    /// <summary>Approved and being sent to the gateway.</summary>
    Approved = 1,

    /// <summary>Submitted to the gateway. Awaiting confirmation.</summary>
    Processing = 2,

    /// <summary>The gateway confirmed the money went back.</summary>
    Completed = 3,

    /// <summary>Refused by whoever reviewed it.</summary>
    Rejected = 4,

    /// <summary>The gateway refused or errored.</summary>
    Failed = 5,

    /// <summary>Withdrawn before a decision.</summary>
    Cancelled = 6
}
