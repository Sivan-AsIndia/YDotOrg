using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Configuration;

/// <summary>
/// The Test button, per provider.
///
/// WHAT IT ACTUALLY DOES FOR RAZORPAY: creates an order for one rupee. That is the same first
/// call the donation path makes, it reserves nothing, it charges nobody, and it fails in exactly
/// the ways a misconfigured merchant account fails - wrong key, wrong secret, a live key against
/// a test dashboard, an account the provider has not activated yet. The order is left where it
/// is; Razorpay expires an unpaid order on its own and the receipt field marks it plainly as a
/// configuration test, so nobody reading the dashboard later mistakes it for a donation.
///
/// A CHEAPER CHECK WAS AVAILABLE AND IS NOT USED. <c>GET /v1/payments?count=1</c> also
/// authenticates, and it would pass on an account that has API access but is not permitted to
/// create orders - which is a real state a new Razorpay account sits in, and the exact state an
/// operator needs this button to catch. Testing with the call that matters is the point.
///
/// FOR THE PROVIDERS WITH NO ADAPTER the honest answer is that this platform cannot yet take a
/// payment through them, and that is what the message says. It still checks what can be checked
/// locally - both halves of the credential present, the key prefix matching the environment -
/// because the commonest failure on this form is a live key pasted into a sandbox row, and that
/// is catchable without a network call at all.
/// </summary>
public sealed class PaymentGatewayConnectivityTester(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentGatewaySettings> settings,
    ILogger<PaymentGatewayConnectivityTester> logger) : IPaymentGatewayConnectivityTester
{
    internal const string HttpClientName = "payment-gateway-test";

    private const string RazorpayOrdersUrl = "https://api.razorpay.com/v1/orders";

    private readonly PaymentGatewaySettings _settings = settings.Value;

    public async Task<GatewayTestOutcome> TestAsync(
        PaymentGatewayConfiguration configuration,
        string? apiKey,
        string? secretKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // THE LOCAL CHECKS RUN FIRST, FOR EVERY PROVIDER. A key that is obviously for the wrong
        // environment should be reported as that rather than as whatever the provider says when
        // it refuses it - "Authentication failed" sends somebody looking at the secret.
        var local = CheckLocally(configuration, apiKey, secretKey);

        if (local is not null)
        {
            return local;
        }

        if (!_settings.AllowOutboundTest)
        {
            return GatewayTestOutcome.Pass(
                "The stored credentials look complete and match this environment. This "
                + "deployment does not allow outbound calls, so the provider was not contacted "
                + "and only the details held here were checked.");
        }

        return configuration.Provider switch
        {
            PaymentGatewayProvider.Razorpay =>
                await TestRazorpayAsync(configuration, apiKey!, secretKey!, cancellationToken),

            // The rest have no adapter in PAY, so a live call would prove a credential this
            // platform still cannot use. Saying so is more useful than a green tick.
            _ => GatewayTestOutcome.Pass(
                $"The stored credentials look complete. {Describe(configuration.Provider)} has no "
                + "native adapter in this platform yet, so donations through it would fall back "
                + "to the generic hosted-checkout flow. The provider was not contacted.")
        };
    }

    /// <summary>
    /// Everything that can be decided without leaving the building.
    ///
    /// Returns null when there is nothing to complain about, so the caller can go on to the
    /// network. A non-null result is always a failure or a qualified pass.
    /// </summary>
    private static GatewayTestOutcome? CheckLocally(
        PaymentGatewayConfiguration configuration, string? apiKey, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return GatewayTestOutcome.Fail(
                "No API key is stored for this gateway, so there is nothing to test with.");
        }

        if (string.IsNullOrWhiteSpace(secretKey)
            && configuration.Provider is not PaymentGatewayProvider.HostedCheckout)
        {
            return GatewayTestOutcome.Fail(
                "No secret key is stored. The provider will refuse every request as "
                + "unauthenticated until one is entered.");
        }

        var descriptor = Application.Common.Constants.PaymentGatewayCatalogue.Find(configuration.Provider);

        // THE MISTAKE THIS CATCHES IS THE EXPENSIVE ONE. A live key in a row marked Sandbox is a
        // row somebody will happily test against and then activate, and the first real donation
        // is where they find out. The reverse - a test key in Production - takes no money at all
        // and is caught the first time a donor tries.
        if (descriptor?.LiveKeyPrefix is { Length: > 0 } livePrefix
            && configuration.Environment == PaymentGatewayEnvironment.Sandbox
            && apiKey.StartsWith(livePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return GatewayTestOutcome.Fail(
                $"This is a LIVE {descriptor.Name} key in a row marked Sandbox. A live key moves "
                + "real money whatever the row says. Change the environment to Production, or "
                + "paste the test key instead.");
        }

        if (descriptor?.TestKeyPrefix is { Length: > 0 } testPrefix
            && configuration.Environment == PaymentGatewayEnvironment.Production
            && apiKey.StartsWith(testPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return GatewayTestOutcome.Fail(
                $"This is a TEST {descriptor.Name} key in a row marked Production. No donation "
                + "through it would ever reach the bank account.");
        }

        return null;
    }

    /// <summary>
    /// Creates a one-rupee Razorpay order with the stored credentials.
    ///
    /// AUTHENTICATION IS HTTP BASIC, key id as the user and key secret as the password, which is
    /// what Razorpay's API wants. The credential is assembled here, used once, and never logged -
    /// not on success, not in the exception handler, and not in the message returned to the
    /// screen.
    /// </summary>
    private async Task<GatewayTestOutcome> TestRazorpayAsync(
        PaymentGatewayConfiguration configuration,
        string apiKey,
        string secretKey,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TestTimeoutSeconds, 3, 60));

            using var request = new HttpRequestMessage(HttpMethod.Post, RazorpayOrdersUrl);

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}")));

            // ONE RUPEE, IN PAISE. Razorpay works exclusively in the smallest unit, and its
            // minimum order is 100 paise. Nothing is charged - an order is a statement of what
            // the merchant expects, not a payment - and nobody is ever sent to pay it.
            request.Content = JsonContent.Create(new
            {
                amount = 100,
                currency = string.IsNullOrWhiteSpace(configuration.SettlementCurrencyCode)
                    ? "INR"
                    : configuration.SettlementCurrencyCode,

                // THE RECEIPT IS WHAT STOPS THIS LOOKING LIKE A DONATION on the Razorpay
                // dashboard. Capped at 40 characters by Razorpay, so the timestamp is short.
                receipt = "ydot-cfg-test-"
                          + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),

                notes = new
                {
                    purpose = "YDot payment gateway configuration test. Not a donation.",
                    configuration_id = configuration.Id.ToString()
                }
            });

            using var response = await client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return GatewayTestOutcome.Pass(
                    "Razorpay accepted these credentials and created a test order for "
                    + "1.00 (nothing was charged, and no donor was involved). This merchant "
                    + "account is ready to take donations.",
                    ReadOrderId(body),
                    stopwatch.ElapsedMilliseconds);
            }

            return GatewayTestOutcome.Fail(DescribeFailure(response.StatusCode, body));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return GatewayTestOutcome.Fail(
                $"Razorpay did not answer within {_settings.TestTimeoutSeconds} seconds. That is "
                + "usually the network between this server and the provider rather than the "
                + "credentials.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // The message, not the exception, and no credential anywhere near it.
            logger.LogWarning(
                "The Razorpay configuration test for gateway {ConfigurationId} could not reach "
                + "the provider: {Reason}",
                configuration.Id,
                exception.Message);

            return GatewayTestOutcome.Fail(
                "This server could not reach Razorpay. Check that outbound HTTPS is allowed from "
                + "the payments network before looking at the credentials.");
        }
    }

    /// <summary>
    /// Turns a provider's refusal into something an operator can act on.
    ///
    /// THE PROVIDER'S OWN WORDING IS PASSED THROUGH where there is any, because "The api key
    /// provided is invalid" is more use than anything this layer could paraphrase. What is added
    /// is the sentence saying WHICH field to look at, which the provider never says.
    /// </summary>
    private static string DescribeFailure(HttpStatusCode statusCode, string body)
    {
        var providerMessage = ReadErrorDescription(body);

        var prefix = statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Razorpay rejected these credentials. Check the key id and the key secret - they "
                + "are issued as a pair and a key from one dashboard will not work with a secret "
                + "from another.",

            HttpStatusCode.BadRequest =>
                "Razorpay accepted the credentials but refused the test order.",

            HttpStatusCode.Forbidden =>
                "These credentials are valid but the account is not permitted to create orders. "
                + "That usually means the Razorpay account has not been activated yet.",

            HttpStatusCode.TooManyRequests =>
                "Razorpay is rate-limiting this account. Wait a moment and test again.",

            _ => $"Razorpay answered {(int)statusCode}."
        };

        return string.IsNullOrWhiteSpace(providerMessage)
            ? prefix
            : $"{prefix} Razorpay said: {providerMessage}";
    }

    /// <summary>The order id from a successful response, for the operator to match on the dashboard.</summary>
    private static string? ReadOrderId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Razorpay's own description of what went wrong: <c>{ error: { description } }</c>.</summary>
    private static string? ReadErrorDescription(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("description", out var description))
            {
                return description.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            // A gateway or proxy answered with HTML. Nothing useful in it, and echoing a page of
            // markup onto the screen would be worse than saying nothing.
            return null;
        }
    }

    private static string Describe(PaymentGatewayProvider provider) =>
        Application.Common.Constants.PaymentGatewayCatalogue.Find(provider)?.Name ?? provider.ToString();
}
