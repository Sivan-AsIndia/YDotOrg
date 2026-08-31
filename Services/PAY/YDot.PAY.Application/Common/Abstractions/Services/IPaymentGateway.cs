using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// The payment provider, behind an interface.
///
/// WHY THE ABSTRACTION IS WORTH IT HERE even though most abstractions over a single vendor are
/// not. Three reasons, and all three are real rather than speculative: each Organisation
/// configures its own gateway account so the concrete provider genuinely varies per request; a
/// module that cannot be exercised without a live merchant account cannot be developed against;
/// and the failure semantics below - particularly the difference between "declined" and
/// "unknown" - are the module's own rules rather than any one provider's.
///
/// NOTHING HERE TAKES A SECRET. The implementation resolves the merchant credentials from the
/// secret store using the reference on <see cref="PaymentGatewayAccount"/>, so a key never
/// travels through the application layer.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Which provider this implementation speaks to. Matched against the account row.</summary>
    string GatewayName { get; }

    /// <summary>
    /// Creates a payment link for an intent.
    ///
    /// <paramref name="idempotencyKey"/> is passed to the provider, so calling this twice for
    /// one attempt yields one payment rather than two.
    /// </summary>
    Task<GatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider what actually happened to an attempt.
    ///
    /// THE MOST IMPORTANT METHOD ON THIS INTERFACE. It is how a TIMED-OUT attempt is resolved
    /// without guessing: retrying an attempt that actually succeeded charges the donor twice, so
    /// an unknown outcome is verified rather than assumed.
    /// </summary>
    Task<GatewayVerificationResult> VerifyPaymentAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        CancellationToken cancellationToken);

    /// <summary>Submits a refund. Partial refunds are supported by amount.</summary>
    Task<GatewayRefundResult> RefundAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        MoneyValue amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies a webhook signature.
    ///
    /// ANYBODY CAN POST TO A WEBHOOK URL. The signature is the only thing that says the provider
    /// sent it, so an event that fails this is stored and never acted on.
    /// </summary>
    bool VerifyWebhookSignature(
        PaymentGatewayAccount account, string payload, string signatureHeader);

    /// <summary>Turns a raw webhook body into the shape the event queue stores.</summary>
    GatewayWebhookEvent? ParseWebhook(string payload);
}

/// <summary>A payment link, or the reason one could not be created.</summary>
public sealed record GatewayLinkResult(
    bool Succeeded,
    string? PaymentLinkUrl,
    string? GatewayReference,
    DateTimeOffset? ExpiresAtUtc,
    string? FailureCode,
    string? FailureMessage)
{
    public static GatewayLinkResult Ok(string url, string reference, DateTimeOffset? expiresAtUtc) =>
        new(true, url, reference, expiresAtUtc, null, null);

    public static GatewayLinkResult Failed(string code, string message) =>
        new(false, null, null, null, code, message);
}

/// <summary>
/// What the provider says about an attempt.
///
/// <see cref="Status"/> IS DELIBERATELY THE FULL ENUM rather than a boolean. "We do not know
/// yet" is a distinct and important answer - see <see cref="PaymentAttemptStatus.Pending"/> -
/// and collapsing it into false would turn an unresolved payment into a reported failure.
/// </summary>
public sealed record GatewayVerificationResult(
    PaymentAttemptStatus Status,
    MoneyValue? CapturedAmount,
    MoneyValue? Fee,
    PaymentMethodType? MethodType,
    string? MaskedInstrument,
    DateTimeOffset? CapturedAtUtc,
    string? ResultCode,
    string? Message);

/// <summary>The outcome of a refund submission.</summary>
public sealed record GatewayRefundResult(
    bool Accepted,
    string? GatewayRefundReference,
    string? FailureCode,
    string? FailureMessage)
{
    public static GatewayRefundResult Ok(string reference) => new(true, reference, null, null);

    public static GatewayRefundResult Failed(string code, string message) =>
        new(false, null, code, message);
}

/// <summary>A parsed webhook, before it is stored in the event queue.</summary>
public sealed record GatewayWebhookEvent(
    string GatewayEventId,
    PaymentEventType EventType,
    string? GatewayReference,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset OccurredAtUtc);
