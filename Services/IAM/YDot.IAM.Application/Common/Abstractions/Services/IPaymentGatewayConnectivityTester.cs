using YDot.IAM.Domain.Entities.Configuration;

namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// What the Test button does.
///
/// IT DOES NOT MOVE MONEY, AND THE DISTINCTION IS WORTH BEING EXACT ABOUT. The screen asks
/// "would a donation work with these credentials?", and the honest way to answer it is to make
/// the same authenticated call the donation path makes FIRST - Razorpay's create-order, which
/// reserves nothing, charges nobody, and fails in precisely the ways a misconfigured account
/// fails: wrong key, wrong secret, live key against a test dashboard, account not activated.
///
/// A configuration screen that actually charged a card to prove itself would need a card, a
/// donor, and a refund afterwards. This gets the same answer from the merchant account alone.
///
/// WHAT A FAILURE MEANS. <see cref="GatewayTestOutcome.Succeeded"/> false with a message the
/// provider gave. The message is passed through as-is because the provider's own wording -
/// "Authentication failed", "The api key provided is invalid" - is more use to whoever is
/// fixing it than anything this layer could paraphrase. It never contains the credential; the
/// implementations check.
/// </summary>
public interface IPaymentGatewayConnectivityTester
{
    /// <summary>
    /// Reaches the provider with this configuration's stored credentials.
    ///
    /// The unsealed credentials are passed in rather than read here, so this service never
    /// touches the protector and one place - the command handler - decides who may unseal.
    /// </summary>
    Task<GatewayTestOutcome> TestAsync(
        PaymentGatewayConfiguration configuration,
        string? apiKey,
        string? secretKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of a test. Never carries a credential, and is safe to store on the configuration
/// row and show on screen.
/// </summary>
/// <param name="Succeeded">True only when the provider answered and accepted the credentials.</param>
/// <param name="Message">What to show the operator. The provider's own words where there are any.</param>
/// <param name="Reference">
/// What the provider created, where it created something - a Razorpay order id. Worth recording:
/// it is how somebody confirms on the provider's dashboard that the call really landed on the
/// account they think it did.
/// </param>
/// <param name="DurationMilliseconds">How long the round trip took, for a slow-gateway report.</param>
public sealed record GatewayTestOutcome(
    bool Succeeded,
    string Message,
    string? Reference = null,
    long DurationMilliseconds = 0)
{
    public static GatewayTestOutcome Fail(string message) => new(false, message);

    public static GatewayTestOutcome Pass(string message, string? reference = null, long elapsedMs = 0) =>
        new(true, message, reference, elapsedMs);
}
