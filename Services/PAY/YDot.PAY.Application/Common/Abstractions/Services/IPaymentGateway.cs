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
    /// Opens a CHECKOUT SESSION for an intent - an order held at the provider that the donor's
    /// own browser then pays against, on our page.
    ///
    /// HOW IT DIFFERS FROM <see cref="CreatePaymentLinkAsync"/>, AND WHY BOTH EXIST. A payment
    /// link is a URL the donor is sent away to, and which the provider also e-mails them; the
    /// donor leaves our site, pays on the provider's, and comes back only if a callback was
    /// configured and reached. A checkout session never leaves: the provider's own script draws
    /// its card form over our page, the donor pays, and we decide where they go next. For a
    /// staff member entering a donation on a donor's behalf - and for any donor sitting in front
    /// of the form right now - that is the flow that matches what they are doing. The link stays
    /// for the cases it is genuinely better at: an e-mail to somebody who is not at a screen, and
    /// any provider that cannot do an in-page checkout.
    ///
    /// THE AMOUNT IS DECIDED HERE AND HELD BY THE PROVIDER, which is the security property that
    /// makes an in-page checkout safe at all. The browser is told the order's id, not its price;
    /// a page that edits the figure it was handed still pays what the order says.
    ///
    /// THE PUBLIC KEY IS PUBLIC BY DESIGN and is the only credential that crosses to the browser.
    /// It identifies the merchant so the provider's script knows whose checkout to draw, and it
    /// authorises nothing on its own - every operation that moves money needs the SECRET, which
    /// stays here. Returning it per organisation, from the account's own configured credential,
    /// is also what keeps one charity's key out of another's page.
    ///
    /// AN IMPLEMENTATION THAT CANNOT DO THIS SAYS SO rather than throwing, by returning
    /// <see cref="GatewayCheckoutSession.NotSupported"/>. The caller falls back to the link,
    /// which every provider here can do.
    /// </summary>
    Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the browser's account of a completed checkout is genuinely the provider's.
    ///
    /// THIS IS THE WHOLE REASON AN IN-PAGE CHECKOUT CAN BE TRUSTED. When the provider's script
    /// finishes it hands the page a payment id, the order id and a signature over the pair, made
    /// with the merchant secret the browser has never seen. Checking it here is what separates
    /// "the provider says this was paid" from "a script on the donor's machine says so" - and
    /// the browser is the one party in the exchange with a motive to lie.
    ///
    /// IT PROVES THE MESSAGE, NOT THE MONEY. A valid signature says this payment id belongs to
    /// this order and came from the provider; it does not say the payment was captured. The
    /// caller still verifies the outcome through <see cref="VerifyPaymentAsync"/>, which asks the
    /// provider directly.
    ///
    /// FAILS CLOSED, always: no secret, a malformed signature or an implementation that does not
    /// support checkout all return false.
    /// </summary>
    bool VerifyCheckoutSignature(
        PaymentGatewayAccount account,
        string orderReference,
        string paymentReference,
        string signature);

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
/// An open checkout session: what the browser needs to draw the provider's payment form, and
/// nothing else.
///
/// <see cref="AmountMinorUnits"/> IS FOR DISPLAY ONLY. The provider charges what the order says;
/// this travels so the page can show a figure before the form opens without a second call.
/// </summary>
public sealed record GatewayCheckoutSession(
    bool Succeeded,
    string? OrderReference,
    string? PublicKey,
    long AmountMinorUnits,
    string? CurrencyCode,
    string? FailureCode,
    string? FailureMessage)
{
    /// <summary>The failure code a caller matches on to fall back to a payment link.</summary>
    public const string NotSupportedCode = "CHECKOUT_NOT_SUPPORTED";

    public static GatewayCheckoutSession Ok(
        string orderReference, string publicKey, long amountMinorUnits, string currencyCode) =>
        new(true, orderReference, publicKey, amountMinorUnits, currencyCode, null, null);

    public static GatewayCheckoutSession Failed(string code, string message) =>
        new(false, null, null, 0, null, code, message);

    /// <summary>
    /// This provider does not do in-page checkout. Not an error: the caller issues a payment
    /// link instead, and the donation proceeds.
    /// </summary>
    public static GatewayCheckoutSession NotSupported(string gatewayName) =>
        Failed(NotSupportedCode, $"{gatewayName} does not support an in-page checkout.");
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
