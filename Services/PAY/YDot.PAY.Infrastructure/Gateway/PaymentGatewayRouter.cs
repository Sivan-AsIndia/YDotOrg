using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Picks the right payment provider for each organisation.
///
/// WHY THIS EXISTS. Three command handlers take a single <see cref="IPaymentGateway"/>, and until
/// now exactly one implementation was registered - so every organisation on the platform spoke
/// whatever protocol that one implementation spoke, regardless of which provider their gateway
/// account named. An organisation configured for Razorpay had its calls sent in a shape Razorpay
/// has never implemented, and every one of them answered 404.
///
/// THE ACCOUNT DECIDES. <c>PaymentGatewayAccount.GatewayName</c> already exists and already says
/// which provider an organisation uses; this reads it and dispatches. Adding a provider is
/// therefore a new adapter and one line of registration, and none of the handlers change.
///
/// AN UNRECOGNISED NAME FALLS BACK rather than failing. <see cref="HostedCheckoutGateway"/> speaks
/// the generic hosted-checkout shape, which is what a deployment pointing at its own payment
/// service or a sandbox will be using - and refusing to take a donation because a name was spelled
/// differently would be the wrong way round.
/// </summary>
public sealed class PaymentGatewayRouter : IPaymentGateway
{
    private readonly IReadOnlyDictionary<string, IPaymentGateway> _byName;
    private readonly IPaymentGateway _fallback;
    private readonly ILogger<PaymentGatewayRouter> _logger;

    public PaymentGatewayRouter(
        RazorpayGateway razorpay,
        HostedCheckoutGateway hostedCheckout,
        ILogger<PaymentGatewayRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(razorpay);
        ArgumentNullException.ThrowIfNull(hostedCheckout);

        _logger = logger;
        _fallback = hostedCheckout;

        _byName = new Dictionary<string, IPaymentGateway>(StringComparer.OrdinalIgnoreCase)
        {
            [RazorpayGateway.ProviderName] = razorpay,
            [hostedCheckout.GatewayName] = hostedCheckout
        };

        // The order webhook parsing is attempted in. Razorpay first because its envelope is
        // unmistakable - `entity: "event"` with a named `event` - so it either recognises a body
        // or declines it cleanly, and the generic parser is the one that accepts most shapes.
        _parsersInOrder = [razorpay, hostedCheckout];
    }

    private readonly IReadOnlyList<IPaymentGateway> _parsersInOrder;

    /// <summary>
    /// The router's own name, which is never written anywhere.
    ///
    /// Every path that records a gateway name has an account in hand and uses the account's, so
    /// this is only what a log line falls back to.
    /// </summary>
    public string GatewayName => "Router";

    public Task<GatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        For(account).CreatePaymentLinkAsync(account, intent, idempotencyKey, cancellationToken);

    public Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        For(account).CreateCheckoutSessionAsync(account, intent, idempotencyKey, cancellationToken);

    public bool VerifyCheckoutSignature(
        PaymentGatewayAccount account,
        string orderReference,
        string paymentReference,
        string signature) =>
        For(account).VerifyCheckoutSignature(account, orderReference, paymentReference, signature);

    public Task<GatewayVerificationResult> VerifyPaymentAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        CancellationToken cancellationToken) =>
        For(account).VerifyPaymentAsync(account, gatewayReference, cancellationToken);

    public Task<GatewayRefundResult> RefundAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        MoneyValue amount,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        For(account).RefundAsync(account, gatewayReference, amount, idempotencyKey, cancellationToken);

    public bool VerifyWebhookSignature(
        PaymentGatewayAccount account, string payload, string signatureHeader) =>
        For(account).VerifyWebhookSignature(account, payload, signatureHeader);

    /// <summary>
    /// Reads a webhook body without knowing whose it is.
    ///
    /// THERE IS NO ACCOUNT AT THIS POINT, and that is not an oversight: the platform works out
    /// which organisation a webhook belongs to FROM the payment reference inside it, so the body
    /// has to be read before the account is known. Each adapter is asked in turn and the first one
    /// that recognises the shape answers.
    /// </summary>
    public GatewayWebhookEvent? ParseWebhook(string payload)
    {
        foreach (var gateway in _parsersInOrder)
        {
            var parsed = gateway.ParseWebhook(payload);

            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private IPaymentGateway For(PaymentGatewayAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!string.IsNullOrWhiteSpace(account.GatewayName)
            && _byName.TryGetValue(account.GatewayName.Trim(), out var gateway))
        {
            return gateway;
        }

        _logger.LogWarning(
            "Gateway account {AccountId} for merchant {MerchantId} names the provider "
            + "'{GatewayName}', for which no adapter is registered. Falling back to the generic "
            + "hosted-checkout adapter, which will not work against a provider expecting its own "
            + "API shape.",
            account.Id,
            account.MerchantId,
            account.GatewayName);

        return _fallback;
    }
}
