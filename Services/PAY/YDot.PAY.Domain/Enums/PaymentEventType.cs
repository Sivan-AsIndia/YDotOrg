namespace YDot.PAY.Domain.Enums;

/// <summary>
/// What a gateway told us, as recorded in the payment event queue.
///
/// EVERY CALLBACK IS STORED BEFORE IT IS ACTED ON. A gateway may deliver the same webhook
/// twice, out of order, or after we already learned the outcome by polling - so the queue is
/// the raw record and the intent status is the interpretation.
/// </summary>
public enum PaymentEventType
{
    Authorised = 0,
    Captured = 1,
    Failed = 2,
    Cancelled = 3,
    Expired = 4,
    Refunded = 5,
    PartiallyRefunded = 6,
    ChargebackOpened = 7,
    ChargebackWon = 8,
    ChargebackLost = 9,
    Settled = 10,

    /// <summary>Something the integration does not recognise. Stored, never acted on.</summary>
    Unknown = 11
}
