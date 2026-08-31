using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// One thing a gateway told us, stored exactly as it arrived.
///
/// EVERY CALLBACK IS RECORDED BEFORE IT IS ACTED ON, and that ordering is the whole design. A
/// gateway may deliver the same webhook twice, deliver them out of order, or deliver one after
/// we already learned the outcome by polling. Storing first and interpreting second means a
/// duplicate is detectable, a late arrival is explainable, and a webhook that crashed the
/// handler is still on disk to be replayed.
///
/// THE RAW PAYLOAD IS KEPT. When a gateway integration misbehaves, the only thing that settles
/// an argument with the provider is what they actually sent.
/// </summary>
public sealed class PaymentEvent : TenantEntity
{
    /// <summary>The attempt this event concerns. Null when it could not be matched to one.</summary>
    public Guid? PaymentAttemptId { get; set; }

    public PaymentAttempt? PaymentAttempt { get; set; }

    /// <summary>The intent, resolved where possible even if the attempt could not be.</summary>
    public Guid? DonationIntentId { get; set; }

    public PaymentEventType EventType { get; set; }

    public PaymentEventStatus Status { get; set; } = PaymentEventStatus.Pending;

    public string GatewayName { get; set; } = string.Empty;

    /// <summary>
    /// The gateway's own id for the EVENT, not for the payment.
    ///
    /// UNIQUE PER GATEWAY, and that constraint is what makes duplicate delivery harmless: the
    /// second insert of the same event id is refused by the database rather than being applied
    /// twice.
    /// </summary>
    public string GatewayEventId { get; set; } = string.Empty;

    /// <summary>The gateway's payment reference, so an unmatched event can still be traced.</summary>
    public string? GatewayReference { get; set; }

    /// <summary>The amount the event refers to. Null for events that carry none.</summary>
    public MoneyValue? Amount { get; set; }

    /// <summary>When the GATEWAY says it happened, which is not when we received it.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>The verbatim webhook body. See the class comment.</summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// Whether the webhook signature verified.
    ///
    /// AN UNVERIFIED EVENT IS STORED BUT NEVER ACTED ON. Anybody can post to a webhook URL; the
    /// signature is the only thing that says the gateway sent it. Storing it anyway means an
    /// attempted forgery leaves a trace.
    /// </summary>
    public bool SignatureVerified { get; set; }

    /// <summary>Why processing failed, when it did. Read from the payment event queue screen.</summary>
    public string? ProcessingError { get; set; }

    public int ProcessingAttempts { get; set; }

    /// <summary>Who dismissed it, for an event an operator decided needed no action.</summary>
    public Guid? DismissedByUserId { get; set; }

    public string? DismissalReason { get; set; }

    /// <summary>True when this event still needs somebody or something to act on it.</summary>
    public bool IsOutstanding => Status is PaymentEventStatus.Pending or PaymentEventStatus.Failed;
}
