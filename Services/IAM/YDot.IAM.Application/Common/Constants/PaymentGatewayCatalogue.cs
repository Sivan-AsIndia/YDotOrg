using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// What the configuration screen needs to know about each provider before anybody has chosen
/// one: what its credentials are called, whether the platform can actually take money through
/// it, and which of its events are worth subscribing to.
///
/// WHY THIS IS SERVED FROM THE API RATHER THAN HARD-CODED IN THE ANGULAR BUNDLE. The half that
/// matters is <see cref="ProviderDescriptor.HasAdapter"/>: whether PAY has an adapter that
/// speaks the provider's own API. That is a fact about the deployed back end, and a copy of it
/// in the client would go stale the first time an adapter shipped - leaving the form either
/// hiding a provider that now works or promising one that does not.
///
/// THE CREDENTIAL LABELS ARE NOT COSMETIC EITHER. Razorpay issues a "Key Id" and a "Key
/// Secret"; Stripe issues a "Publishable key" and a "Secret key"; PayPal calls the pair "Client
/// ID" and "Client Secret". An operator holding the provider's dashboard in the other window is
/// looking for the provider's words, and a form that says "API Key" at them is a form they have
/// to guess at.
/// </summary>
public static class PaymentGatewayCatalogue
{
    /// <summary>One provider, as the form needs it.</summary>
    /// <param name="Provider">The enum member, which is also what PAY matches an adapter on.</param>
    /// <param name="Name">The provider's own spelling of its name.</param>
    /// <param name="HasAdapter">
    /// True when PAY speaks this provider's API natively. False means a configuration will be
    /// saved and honoured but donations fall through to the generic hosted-checkout adapter,
    /// which no commercial provider implements - so the screen says so rather than letting an
    /// operator discover it from a failed donation.
    /// </param>
    /// <param name="ApiKeyLabel">What the provider calls the public half of the pair.</param>
    /// <param name="SecretKeyLabel">What it calls the secret half. Null when it issues only one.</param>
    /// <param name="MerchantIdLabel">What it calls the account identifier. Null when it has none.</param>
    /// <param name="TestKeyPrefix">
    /// The prefix a sandbox key carries, where the provider uses one. It is what lets the screen
    /// warn that a live key has been pasted into a sandbox row - the single most expensive
    /// mistake this form can absorb.
    /// </param>
    /// <param name="LiveKeyPrefix">The prefix a production key carries, where there is one.</param>
    /// <param name="DocumentationUrl">Where the operator gets the credentials.</param>
    public sealed record ProviderDescriptor(
        PaymentGatewayProvider Provider,
        string Name,
        bool HasAdapter,
        string ApiKeyLabel,
        string? SecretKeyLabel,
        string? MerchantIdLabel,
        string? TestKeyPrefix,
        string? LiveKeyPrefix,
        string? DocumentationUrl);

    /// <summary>
    /// The providers the form offers, in the order it offers them.
    ///
    /// RAZORPAY LEADS because it is the one with an adapter. Ordering by what actually works
    /// rather than alphabetically is what stops somebody configuring Stripe first and then
    /// finding out.
    /// </summary>
    public static readonly IReadOnlyList<ProviderDescriptor> Providers =
    [
        new(PaymentGatewayProvider.Razorpay, "Razorpay", HasAdapter: true,
            "Key Id", "Key Secret", "Merchant Id", "rzp_test_", "rzp_live_",
            "https://dashboard.razorpay.com/app/website-app-settings/api-keys"),

        new(PaymentGatewayProvider.Stripe, "Stripe", HasAdapter: false,
            "Publishable key", "Secret key", "Account Id", "pk_test_", "pk_live_",
            "https://dashboard.stripe.com/apikeys"),

        new(PaymentGatewayProvider.PayPal, "PayPal", HasAdapter: false,
            "Client ID", "Client Secret", "Merchant Id", null, null,
            "https://developer.paypal.com/dashboard/applications"),

        new(PaymentGatewayProvider.PayU, "PayU", HasAdapter: false,
            "Merchant Key", "Merchant Salt", "Merchant Id", null, null,
            "https://onboarding.payu.in/app/account/dashboard"),

        new(PaymentGatewayProvider.Cashfree, "Cashfree", HasAdapter: false,
            "App Id", "Secret Key", "Merchant Id", "TEST", null,
            "https://merchant.cashfree.com/merchants/pg/developers"),

        // The deployment's own endpoint. PAY's HostedCheckoutGateway speaks this, so it does have
        // an adapter - it is simply not a commercial provider.
        new(PaymentGatewayProvider.HostedCheckout, "Hosted checkout (self-hosted)", HasAdapter: true,
            "API Key", "Secret", "Merchant Id", null, null, null)
    ];

    /// <summary>
    /// The webhook events the form offers to subscribe to.
    ///
    /// THE THREE THE BRIEF NAMES, plus the two a refund and a chargeback actually need. Stored
    /// as this platform's own vocabulary rather than any one provider's spelling: PAY's webhook
    /// parsers already normalise <c>payment.captured</c>, <c>charge.succeeded</c> and the rest
    /// into a single event type, and a stored list of raw provider strings would have to be
    /// re-mapped every time a provider renamed one.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Description)> WebhookEvents =
    [
        ("payment.success", "Payment success",
            "The donor paid. This is the one that turns an intent into a donation and issues a receipt."),
        ("payment.failure", "Payment failure",
            "The payment was attempted and refused. Puts the attempt on the payment queue."),
        ("payment.pending", "Payment pending",
            "The provider has taken the instruction but not settled it yet, as with a bank transfer."),
        ("refund.processed", "Refund processed",
            "Money went back to the donor. Closes the refund case and voids the receipt."),
        ("chargeback.created", "Chargeback raised",
            "The donor's bank has disputed the payment. Opens a chargeback case.")
    ];

    /// <summary>
    /// Payment methods a configuration may narrow itself to.
    ///
    /// EMPTY MEANS "whatever the merchant account allows", which is the right default: an
    /// organisation that has not thought about it should not silently have UPI switched off.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> PaymentMethods =
    [
        ("card", "Cards"),
        ("upi", "UPI"),
        ("netbanking", "Net banking"),
        ("wallet", "Wallets"),
        ("emi", "EMI"),
        ("bank_transfer", "Bank transfer")
    ];

    /// <summary>The descriptor for a provider, or null when the enum member has no entry.</summary>
    public static ProviderDescriptor? Find(PaymentGatewayProvider provider) =>
        Providers.FirstOrDefault(item => item.Provider == provider);

    /// <summary>True when PAY can speak this provider's own API.</summary>
    public static bool HasAdapter(PaymentGatewayProvider provider) =>
        Find(provider)?.HasAdapter ?? false;

    /// <summary>Every event code the form may legitimately send back.</summary>
    public static IReadOnlyList<string> WebhookEventCodes =>
        [.. WebhookEvents.Select(item => item.Code)];

    /// <summary>Every method code the form may legitimately send back.</summary>
    public static IReadOnlyList<string> PaymentMethodCodes =>
        [.. PaymentMethods.Select(item => item.Code)];
}
