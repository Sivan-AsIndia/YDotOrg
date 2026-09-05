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
/// which provider an organisation uses - and where the Organisation has filled in IAM's payment
/// gateway configuration screen, that name is the <c>Provider</c> column of
/// <c>PaymentGatewayConfig</c>, written over the account by
/// <see cref="ConfiguredGatewayAccountRepository"/>. This reads it and dispatches.
///
/// THE TABLE IS BUILT FROM WHATEVER IS REGISTERED, which is the change that makes "add Stripe"
/// a one-line job. Every <see cref="IPaymentGatewayAdapter"/> in the container is indexed by its
/// own <c>GatewayName</c>, so a new provider is a new adapter class plus its registration - this
/// file does not change, no handler changes, and no administrator has to wait for a deployment
/// that names their provider in a constructor.
///
/// WHY A SEPARATE MARKER INTERFACE. The router is itself an <see cref="IPaymentGateway"/>, so
/// asking the container for every <c>IPaymentGateway</c> would ask it to build this class while
/// building this class. Adapters are registered under <see cref="IPaymentGatewayAdapter"/>
/// instead, and the cycle cannot form.
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
        IEnumerable<IPaymentGatewayAdapter> adapters,
        HostedCheckoutGateway hostedCheckout,
        ILogger<PaymentGatewayRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(hostedCheckout);

        _logger = logger;
        _fallback = hostedCheckout;

        var registered = adapters.ToList();

        // LAST REGISTRATION WINS for a duplicated name, which is what lets a deployment replace a
        // built-in adapter without removing it. An adapter with no name at all is skipped rather
        // than indexed under the empty string, where it would be selected by every account whose
        // GatewayName had been left blank.
        var byName = new Dictionary<string, IPaymentGateway>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in registered.Where(a => !string.IsNullOrWhiteSpace(a.GatewayName)))
        {
            byName[adapter.GatewayName.Trim()] = adapter;
        }

        // The fallback is always reachable by name too, even if it was never registered as an
        // adapter, so an account naming it explicitly resolves rather than falling through.
        byName.TryAdd(hostedCheckout.GatewayName, hostedCheckout);

        _byName = byName;

        // The order webhook parsing is attempted in. THE SPECIFIC PARSERS RUN BEFORE THE GENERIC
        // ONE: Razorpay's envelope is unmistakable - `entity: "event"` with a named `event` - so
        // it either recognises a body or declines it cleanly, whereas the hosted-checkout parser
        // accepts most shapes and would swallow a body that belongs to somebody else. Ordering by
        // "is this the fallback" keeps that true however many providers are added later.
        _parsersInOrder =
        [
            .. registered.Where(adapter => !ReferenceEquals(adapter, hostedCheckout)),
            hostedCheckout
        ];

        _logger.LogInformation(
            "Payment gateway router initialised with {ProviderCount} provider(s): {Providers}. "
            + "An Organisation is routed by the Provider column of its gateway configuration.",
            _byName.Count,
            string.Join(", ", _byName.Keys.Order()));
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
