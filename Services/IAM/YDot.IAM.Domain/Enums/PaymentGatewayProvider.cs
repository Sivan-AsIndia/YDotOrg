namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The payment providers an Organisation may configure.
///
/// STORED AS A STRING in the database, like every other enum in this service, so a provider
/// added below cannot renumber the ones already written. The value is also what PAY matches
/// against <c>PaymentGatewayAccount.GatewayName</c> when it picks an adapter, which is why the
/// names are spelled exactly as that side spells them.
///
/// NOT EVERY PROVIDER HERE HAS ITS OWN ADAPTER IN PAY. Razorpay does. The rest fall through to
/// PAY's generic hosted-checkout adapter, which speaks a shape a self-hosted or sandbox
/// endpoint understands and no commercial provider implements. Offering them on the form is
/// still right - an Organisation records where its money is meant to go before the adapter
/// exists, and the configuration screen says plainly which are live.
/// </summary>
public enum PaymentGatewayProvider
{
    /// <summary>No provider chosen. The default a new configuration form opens on.</summary>
    None = 0,

    Razorpay = 1,

    Stripe = 2,

    PayPal = 3,

    PayU = 4,

    Cashfree = 5,

    /// <summary>
    /// A deployment's own hosted-checkout endpoint. What a sandbox or an on-premise payment
    /// service uses, and what PAY falls back to for any provider it has no adapter for.
    /// </summary>
    HostedCheckout = 6
}
