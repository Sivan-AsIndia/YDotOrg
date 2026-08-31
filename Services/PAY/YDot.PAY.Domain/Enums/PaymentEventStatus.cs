namespace YDot.PAY.Domain.Enums;

/// <summary>How far a queued gateway event has got through processing.</summary>
public enum PaymentEventStatus
{
    /// <summary>Received and stored. Not yet applied.</summary>
    Pending = 0,

    /// <summary>Applied successfully.</summary>
    Processed = 1,

    /// <summary>
    /// Applied and found to change nothing - a duplicate webhook, or one that arrived after we
    /// already learned the outcome.
    ///
    /// RECORDED RATHER THAN DISCARDED, because "we saw this twice" is a useful thing to know
    /// when a gateway integration starts misbehaving.
    /// </summary>
    Duplicate = 2,

    /// <summary>Processing threw. Sits in the queue for an operator.</summary>
    Failed = 3,

    /// <summary>An operator looked at it and decided no action was needed.</summary>
    Dismissed = 4
}
