using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// One attempt to take the money for an intent.
///
/// A SEPARATE ROW PER ATTEMPT, not a status on the intent. Section 23 allows a failed payment to
/// be retried and repeated failures to be escalated to Payment Support and Safe Retry - and
/// neither is possible without the history of what was tried, on what method, and what the
/// gateway said each time.
///
/// THE GATEWAY REFERENCE IS UNIQUE ACROSS THE PLATFORM. It is the value a webhook arrives
/// carrying, with no session and no Organisation to scope it, so it has to resolve globally -
/// and two Organisations sharing one would credit one charity's money to another.
/// </summary>
public sealed class PaymentAttempt : TenantEntity
{
    public Guid DonationIntentId { get; set; }

    public DonationIntent DonationIntent { get; set; } = default!;

    /// <summary>1 for the first attempt, 2 for the next. Shown in the support timeline.</summary>
    public int AttemptNumber { get; set; }

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Initiated;

    /// <summary>
    /// The gateway's own identifier for this attempt.
    ///
    /// Null until the gateway has been reached at all - an attempt that failed before leaving
    /// our side has no gateway reference, which is itself diagnostic.
    /// </summary>
    public string? GatewayReference { get; set; }

    /// <summary>Which gateway. Recorded per attempt because an organisation may switch providers.</summary>
    public string GatewayName { get; set; } = string.Empty;

    public PaymentMethodType? MethodType { get; set; }

    /// <summary>The masked instrument, for example "**** 4242". NEVER the full number.</summary>
    public string? MaskedInstrument { get; set; }

    /// <summary>What was requested. May differ from what was captured - see Donation.</summary>
    public MoneyValue RequestedAmount { get; set; } = default!;

    /// <summary>What the gateway actually took. Null until capture.</summary>
    public MoneyValue? CapturedAmount { get; set; }

    public DateTimeOffset InitiatedAtUtc { get; set; }

    public DateTimeOffset? AuthorisedAtUtc { get; set; }

    public DateTimeOffset? CapturedAtUtc { get; set; }

    public DateTimeOffset? FailedAtUtc { get; set; }

    /// <summary>The gateway's own code, kept verbatim so support can quote it back to the provider.</summary>
    public string? GatewayResultCode { get; set; }

    public string? GatewayMessage { get; set; }

    /// <summary>
    /// The message shown to the DONOR, which is deliberately not the gateway's.
    ///
    /// A gateway message often names the issuing bank's decline reason, which the donor cannot
    /// act on and which sometimes reveals more about their account than they would want a
    /// charity's website to display.
    /// </summary>
    public string? DonorFacingMessage { get; set; }

    /// <summary>
    /// The idempotency key sent to the gateway.
    ///
    /// THE SINGLE MOST IMPORTANT FIELD FOR SAFE RETRY. Reusing it means the gateway recognises a
    /// repeat of the same attempt and returns the original outcome rather than charging again -
    /// which is what makes retrying a TIMED-OUT attempt safe rather than reckless.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public string? DonorIpAddress { get; set; }

    public string? DonorUserAgent { get; set; }

    public ICollection<PaymentEvent> Events { get; set; } = [];

    /// <summary>True when this attempt took money.</summary>
    public bool IsSuccessful => Status == PaymentAttemptStatus.Succeeded;

    /// <summary>
    /// Whether the outcome is genuinely unknown.
    ///
    /// A TIMED-OUT attempt may or may not have charged the donor, so it must be VERIFIED with
    /// the gateway rather than simply retried - retrying an attempt that actually succeeded
    /// charges twice.
    /// </summary>
    public bool NeedsVerification => Status is PaymentAttemptStatus.TimedOut or PaymentAttemptStatus.Pending;
}
