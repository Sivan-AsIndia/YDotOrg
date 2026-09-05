using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Razorpay, spoken properly.
///
/// WHY A SEPARATE ADAPTER. <see cref="HostedCheckoutGateway"/> speaks a generic hosted-checkout
/// shape - <c>POST payment-links</c> with a body of our own design - which no real provider
/// implements. Pointed at Razorpay it produces a 404 on every call. Razorpay's API differs in
/// every particular that matters: the route names, the field names, the authentication scheme,
/// the webhook envelope and the signature format. So it gets its own adapter rather than a
/// configuration flag.
///
/// WHAT REPLACES WHAT. The browser used to open Razorpay Checkout directly with a test key
/// compiled into the Angular bundle and no order id. Three things were wrong with that and each
/// on its own is disqualifying: the key was readable by anybody who opened the page; a payment
/// with no order id and no server-side verification cannot be tied back to an intent; and the
/// platform learned the outcome from a browser callback, which is the one party in the exchange
/// with an interest in lying about it. The donor now pays on Razorpay's own hosted page and the
/// outcome arrives through the SIGNED webhook.
///
/// CREDENTIALS NEVER APPEAR IN A GATEWAY ACCOUNT. The account carries a REFERENCE - a
/// configuration key - and the key id and secret live wherever the deployment puts its secrets.
/// The reference resolves to <c>PaymentGateways:{reference}:ApiKey</c>, which for Razorpay holds
/// <c>key_id:key_secret</c>, the pair Razorpay's HTTP Basic authentication wants.
///
/// AMOUNTS ARE IN PAISE. Razorpay works exclusively in the smallest unit of the currency, so the
/// conversion happens once, here, at the boundary - sending 500.00 where 50000 was meant is the
/// classic hundred-fold error and it is a hundred-fold error in the donor's favour or ours
/// depending on which way round it goes.
/// </summary>
public sealed class RazorpayGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentSettings> paymentSettings,
    IOptions<ClientAppSettings> clientSettings,
    IGatewayCredentialResolver credentials,
    ILogger<RazorpayGateway> logger) : IPaymentGatewayAdapter
{
    /// <summary>The value a gateway account's <c>GatewayName</c> must hold to reach this adapter.</summary>
    public const string ProviderName = "Razorpay";

    internal const string HttpClientName = "razorpay-gateway";

    /// <summary>Where Razorpay's API lives when the configuration does not say otherwise.</summary>
    private const string DefaultBaseUrl = "https://api.razorpay.com/v1/";

    /// <summary>
    /// Razorpay refuses an expiry inside fifteen minutes, so a shorter one is simply not sent.
    /// A link with no expiry is better than a link the provider rejected outright.
    /// </summary>
    private static readonly TimeSpan MinimumLinkValidity = TimeSpan.FromMinutes(15);

    private readonly PaymentSettings _settings = paymentSettings.Value;
    private readonly ClientAppSettings _client = clientSettings.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string GatewayName => ProviderName;

    // =============================================================================================
    // Opening a checkout session
    // =============================================================================================

    /// <summary>
    /// Creates a Razorpay ORDER, which is what Razorpay Checkout is opened against.
    /// </summary>
    /// <remarks>
    /// AN ORDER IS NOT A PAYMENT LINK, and the difference is the whole point of this method. A
    /// link is a page Razorpay hosts and e-mails; an order is a server-side record of "this
    /// merchant expects this much, for this reference" which the donor pays against WITHOUT
    /// leaving our site - Checkout draws over the page they are already on, and we choose where
    /// they go afterwards. It is also the flow every Razorpay test key is set up for, so a
    /// development machine can complete a payment end to end.
    ///
    /// THE AMOUNT LIVES ON THE ORDER, so the browser cannot change it. This is the reason the
    /// earlier in-page integration was removed and the reason this one is safe: that version
    /// passed an amount from a client signal straight to Checkout with no order behind it, so a
    /// donor could pay one rupee against a ten-thousand intent. Here the browser is handed an
    /// ORDER ID; the price is Razorpay's own copy of what we told it.
    ///
    /// THE RECEIPT FIELD CARRIES THE INTENT REFERENCE. Razorpay caps it at forty characters and
    /// shows it on the dashboard row, which is what lets support start from a Razorpay payment
    /// and reach the donation. Unlike a payment link's reference_id it is NOT enforced unique, so
    /// the double-submit guard stays where it already is - the attempt and version check in the
    /// command handler.
    /// </remarks>
    public async Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(intent);

        var credential = ResolveUsableCredential(account);

        if (credential is null)
        {
            return GatewayCheckoutSession.Failed(
                "GATEWAY_NOT_CONFIGURED",
                "This organisation's Razorpay credentials are not configured.");
        }

        // THE KEY ID, WHICH IS THE HALF THAT MAY BE PUBLISHED. Checkout needs it in the browser to
        // know whose merchant account to draw; the secret after the colon never leaves this
        // process. A credential stored pre-encoded has no colon to split on, and while that is a
        // perfectly good credential for the Basic-auth calls it cannot yield a key id - so
        // checkout is declined rather than guessed at, and the caller falls back to a link.
        var separator = credential.ApiKey.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            logger.LogWarning(
                "The Razorpay credential at PaymentGateways:{Reference} is stored pre-encoded, so "
                + "no key id can be read from it and in-page checkout is unavailable for merchant "
                + "{MerchantId}. A payment link will be used instead.",
                account.ApiKeyReference,
                account.MerchantId);

            return GatewayCheckoutSession.NotSupported(ProviderName);
        }

        var publicKey = credential.ApiKey[..separator];

        var payload = new CreateOrderPayload
        {
            Amount = ToMinorUnits(intent.Amount),
            Currency = intent.Amount.CurrencyCode,

            // Razorpay caps receipt at 40 characters and rejects the whole request over it.
            Receipt = Trim(intent.IntentReference, 40),

            // CAPTURED AUTOMATICALLY. The alternative is an authorisation this platform would then
            // have to capture on a second call - a state a donation has no use for, and one that
            // expires into a refund if anything goes wrong between the two.
            PaymentCapture = true,

            Notes = new Dictionary<string, string>
            {
                ["intent_reference"] = intent.IntentReference,
                ["merchant_id"] = account.MerchantId,
                ["campaign_id"] = intent.CampaignId?.ToString() ?? string.Empty,
                ["idempotency_key"] = idempotencyKey
            }
        };

        try
        {
            var client = CreateClient(credential);

            using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            // Razorpay honours this header on order creation, so a call retried after a timeout
            // returns the SAME order rather than opening a second one against the same intent.
            request.Headers.TryAddWithoutValidation("X-Razorpay-Idempotency-Key", idempotencyKey);

            using var response = await client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Razorpay refused an order for intent {IntentReference}: {StatusCode} {Body}.",
                    intent.IntentReference,
                    (int)response.StatusCode,
                    Truncate(body));

                return GatewayCheckoutSession.Failed(
                    $"RAZORPAY_{(int)response.StatusCode}",
                    ReadError(body) ?? "Razorpay could not start this payment.");
            }

            var order = JsonSerializer.Deserialize<OrderResponse>(body, JsonOptions);

            if (order is null || string.IsNullOrWhiteSpace(order.Id))
            {
                return GatewayCheckoutSession.Failed(
                    "RAZORPAY_BAD_RESPONSE", "Razorpay returned an order we could not read.");
            }

            return GatewayCheckoutSession.Ok(
                order.Id,
                publicKey,
                order.Amount > 0 ? order.Amount : ToMinorUnits(intent.Amount),
                string.IsNullOrWhiteSpace(order.Currency) ? intent.Amount.CurrencyCode : order.Currency);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach Razorpay to open a checkout session for intent {IntentReference}.",
                intent.IntentReference);

            return GatewayCheckoutSession.Failed(
                "GATEWAY_UNREACHABLE", "We could not reach Razorpay to start this payment.");
        }
    }

    /// <summary>
    /// Checks the signature Razorpay Checkout hands back to the browser.
    /// </summary>
    /// <remarks>
    /// HMAC-SHA256 OVER THE ORDER ID, A PIPE AND THE PAYMENT ID, KEYED ON THE KEY SECRET and
    /// hex-encoded - which is Razorpay's documented scheme for a Checkout handler response, and a
    /// different construction from the webhook signature above (raw body, webhook secret).
    /// Confusing the two fails every check, so they stay as two methods rather than one with a
    /// flag.
    ///
    /// THE PIPE IS PART OF THE MESSAGE AND THE ORDER COMES FIRST. Reversing them, or joining with
    /// anything else, produces a valid-looking hash that never matches.
    ///
    /// COMPARED IN CONSTANT TIME. A signature check that returns early on the first wrong byte
    /// leaks how much of a guess was right, which is enough to forge one a byte at a time.
    /// </remarks>
    public bool VerifyCheckoutSignature(
        PaymentGatewayAccount account,
        string orderReference,
        string paymentReference,
        string signature)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(orderReference)
            || string.IsNullOrWhiteSpace(paymentReference)
            || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var credential = ResolveUsableCredential(account);

        var separator = credential?.ApiKey.IndexOf(':', StringComparison.Ordinal) ?? -1;

        if (credential is null || separator <= 0)
        {
            // FAILS CLOSED. Without the secret there is no way to tell Razorpay's word from a
            // fabricated one, and the safe answer to "did this payment happen" is not "yes".
            logger.LogWarning(
                "A checkout confirmation arrived for merchant {MerchantId} but no usable key "
                + "secret is configured, so it cannot be verified.",
                account.MerchantId);

            return false;
        }

        var keySecret = credential.ApiKey[(separator + 1)..];

        try
        {
            var expected = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(keySecret),
                Encoding.UTF8.GetBytes($"{orderReference}|{paymentReference}"));

            var provided = Convert.FromHexString(signature.Trim());

            return provided.Length == expected.Length
                   && CryptographicOperations.FixedTimeEquals(expected, provided);
        }
        catch (FormatException)
        {
            logger.LogWarning(
                "A checkout confirmation for merchant {MerchantId} carried a signature that is "
                + "not hexadecimal, so it was rejected.",
                account.MerchantId);

            return false;
        }
    }

    // =============================================================================================
    // Creating the payment link
    // =============================================================================================

    /// <summary>
    /// Creates a Razorpay Payment Link for one donation intent.
    /// </summary>
    /// <remarks>
    /// THE INTENT REFERENCE GOES IN <c>reference_id</c>, which Razorpay enforces as unique per
    /// account. That is not decoration: it is what makes a second link for the same intent
    /// impossible at the provider as well as here, so a donor cannot end up holding two live links
    /// for one gift even if two operators press the button at the same moment.
    ///
    /// THE RETURNED REFERENCE IS THE PAYMENT LINK'S ID, not a payment id - no payment exists yet.
    /// Verification below knows how to resolve one into the other.
    /// </remarks>
    public async Task<GatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(intent);

        var credential = ResolveUsableCredential(account);

        if (credential is null)
        {
            return GatewayLinkResult.Failed(
                "GATEWAY_NOT_CONFIGURED",
                "This organisation's Razorpay credentials are not configured.");
        }

        var validityMinutes = account.PaymentLinkValidityMinutes > 0
            ? account.PaymentLinkValidityMinutes
            : _settings.DefaultPaymentLinkValidityMinutes;

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(validityMinutes);

        var payload = new CreateLinkPayload
        {
            Amount = ToMinorUnits(intent.Amount),
            Currency = intent.Amount.CurrencyCode,
            AcceptPartial = false,
            Description = Describe(intent),
            ReferenceId = intent.IntentReference,
            Customer = new CustomerPayload
            {
                Name = Trim(intent.DonorName, 100),
                Email = intent.Email,

                // Razorpay validates the contact and refuses the whole request on a malformed one,
                // so a blank or obviously wrong mobile is omitted rather than sent and rejected.
                Contact = NormaliseContact(intent.Mobile)
            },

            // EMAIL ONLY. Razorpay charges for the SMS and an organisation that has not asked for
            // it should not be billed for it by a default nobody chose.
            Notify = new NotifyPayload { Email = !string.IsNullOrWhiteSpace(intent.Email), Sms = false },

            ReminderEnable = false,

            // WHERE THE DONOR COMES BACK TO. Razorpay redirects the donor's own BROWSER here
            // after they pay, appending razorpay_payment_link_reference_id - which is the
            // ReferenceId above, our intent reference - so the result page knows which donation
            // it is looking at without the donor having an account.
            //
            // THIS IS A BROWSER REDIRECT, NOT A SERVER CALL, and the distinction is the whole
            // reason it works where a webhook does not: Razorpay's servers never have to reach
            // us, so http://localhost:6700 is a perfectly good callback on a development
            // machine. The result page then asks US to verify, and verification is a PULL - our
            // server calls GET payment_links/{id} - so the outcome is confirmed with no inbound
            // connectivity anywhere in the loop.
            //
            // IT FALLS BACK TO THE CONFIGURED CLIENT rather than requiring a per-account URL.
            // ReturnUrl on the gateway account is null on every seeded row, so the callback was
            // simply never sent: a donor who paid was left on Razorpay's own page, nothing
            // called verify, and the donation sat Pending until an administrator opened Support
            // & Retry and pressed Verify status. An organisation that needs its own landing page
            // still sets ReturnUrl and that wins.
            CallbackUrl = ResolveCallbackUrl(account),
            CallbackMethod = string.IsNullOrWhiteSpace(ResolveCallbackUrl(account)) ? null : "get",

            ExpireBy = expiresAtUtc - DateTimeOffset.UtcNow >= MinimumLinkValidity
                ? expiresAtUtc.ToUnixTimeSeconds()
                : null,

            // The notes travel with the payment and come back on the webhook, which is what lets a
            // support conversation start from a Razorpay dashboard row and reach our record.
            Notes = new Dictionary<string, string>
            {
                ["intent_reference"] = intent.IntentReference,
                ["merchant_id"] = account.MerchantId,
                ["campaign_id"] = intent.CampaignId?.ToString() ?? string.Empty
            }
        };

        try
        {
            var client = CreateClient(credential);

            using var request = new HttpRequestMessage(HttpMethod.Post, "payment_links")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            // Razorpay does not honour an Idempotency-Key on payment links - `reference_id` is its
            // uniqueness control - but the header costs nothing and is what a proxy or a gateway
            // in front of it would key on.
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

            using var response = await client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = ReadError(body);

                logger.LogWarning(
                    "Razorpay refused a payment link for intent {IntentReference}: {StatusCode} {Body}",
                    intent.IntentReference,
                    (int)response.StatusCode,
                    Truncate(body));

                return GatewayLinkResult.Failed(
                    $"RAZORPAY_{(int)response.StatusCode}",
                    error ?? "Razorpay could not create a payment link. Please try again.");
            }

            var link = JsonSerializer.Deserialize<PaymentLinkResponse>(body, JsonOptions);

            if (link is null
                || string.IsNullOrWhiteSpace(link.Id)
                || string.IsNullOrWhiteSpace(link.ShortUrl))
            {
                return GatewayLinkResult.Failed(
                    "RAZORPAY_BAD_RESPONSE",
                    "Razorpay returned an unusable response. Please try again.");
            }

            return GatewayLinkResult.Ok(
                link.ShortUrl,
                link.Id,
                link.ExpireBy.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(link.ExpireBy.Value)
                    : expiresAtUtc);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach Razorpay to create a link for intent {IntentReference}.",
                intent.IntentReference);

            // NO LINK WAS CREATED, so no money can have moved. This one genuinely is a failure
            // rather than an unknown, and the donor can safely be offered another attempt.
            return GatewayLinkResult.Failed(
                "GATEWAY_UNREACHABLE",
                "We could not reach the payment provider. Please try again in a moment.");
        }
    }

    // =============================================================================================
    // Asking what actually happened
    // =============================================================================================

    /// <summary>
    /// Asks Razorpay what happened to an attempt.
    /// </summary>
    /// <remarks>
    /// TWO KINDS OF REFERENCE REACH THIS METHOD and telling them apart is the whole of the logic.
    /// A reference beginning <c>plink_</c> is a payment LINK, which is what we stored when the link
    /// was created and before anybody paid; one beginning <c>pay_</c> is a payment, which is what a
    /// webhook gives us afterwards. A link is resolved to its most recent payment first, because
    /// the amount, the fee and the instrument all live on the payment.
    ///
    /// A LINK WITH NO PAYMENTS IS PENDING, NOT FAILED. The donor may still be on the page. Only an
    /// expired or cancelled link is a settled negative.
    /// </remarks>
    public async Task<GatewayVerificationResult> VerifyPaymentAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayReference);

        var credential = ResolveUsableCredential(account);

        if (credential is null)
        {
            return Unknown("GATEWAY_NOT_CONFIGURED",
                "Razorpay is not configured for this organisation, so the outcome could not be checked.");
        }

        try
        {
            var client = CreateClient(credential);

            var paymentId = gatewayReference;

            if (gatewayReference.StartsWith("plink_", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await ResolveLinkAsync(client, gatewayReference, cancellationToken);

                if (resolved.Terminal is not null)
                {
                    return resolved.Terminal;
                }

                paymentId = resolved.PaymentId!;
            }

            using var response = await client.GetAsync(
                $"payments/{Uri.EscapeDataString(paymentId)}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Razorpay could not be queried for {GatewayReference}: {StatusCode}.",
                    gatewayReference,
                    (int)response.StatusCode);

                return Unknown(
                    $"RAZORPAY_{(int)response.StatusCode}",
                    "Razorpay could not confirm this payment. It will be checked again.");
            }

            var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(
                JsonOptions, cancellationToken);

            return payment is null
                ? Unknown("RAZORPAY_BAD_RESPONSE", "Razorpay returned an unreadable status.")
                : Describe(payment);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception, "Could not reach Razorpay to verify {GatewayReference}.", gatewayReference);

            // TIMED OUT, NOT FAILED. The difference matters more here than anywhere else in the
            // module: "failed" invites a retry, and a retry against a payment that actually
            // succeeded charges the donor twice.
            return Unknown(
                "GATEWAY_UNREACHABLE",
                "We could not confirm this payment with Razorpay. It will be checked again.");
        }
    }

    /// <summary>
    /// Turns a payment-link id into the payment id underneath it.
    /// </summary>
    /// <returns>
    /// Either a payment id to read, or a terminal answer when the link itself settles the question.
    /// </returns>
    private async Task<(string? PaymentId, GatewayVerificationResult? Terminal)> ResolveLinkAsync(
        HttpClient client, string linkId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"payment_links/{Uri.EscapeDataString(linkId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return (null, Unknown(
                $"RAZORPAY_{(int)response.StatusCode}",
                "Razorpay could not be asked about this payment link. It will be checked again."));
        }

        var link = await response.Content.ReadFromJsonAsync<PaymentLinkResponse>(
            JsonOptions, cancellationToken);

        if (link is null)
        {
            return (null, Unknown("RAZORPAY_BAD_RESPONSE", "Razorpay returned an unreadable link."));
        }

        // The newest payment on the link is the one that matters: a donor who failed once and
        // succeeded on a second try has two, and the second is the truth.
        var latest = link.Payments?
            .Where(payment => !string.IsNullOrWhiteSpace(payment.PaymentId))
            .OrderByDescending(payment => payment.CreatedAt ?? 0)
            .FirstOrDefault();

        if (latest is not null)
        {
            return (latest.PaymentId, null);
        }

        var status = link.Status?.Trim().ToLowerInvariant();

        return status switch
        {
            "expired" => (null, new GatewayVerificationResult(
                PaymentAttemptStatus.Abandoned, null, null, null, null, null,
                "LINK_EXPIRED", "The payment link expired before it was used.")),

            "cancelled" => (null, new GatewayVerificationResult(
                PaymentAttemptStatus.Abandoned, null, null, null, null, null,
                "LINK_CANCELLED", "The payment link was cancelled.")),

            // Created, and nobody has paid yet. The donor may still be on the page.
            _ => (null, new GatewayVerificationResult(
                PaymentAttemptStatus.Pending, null, null, null, null, null,
                "LINK_UNPAID", "No payment has been made against this link yet."))
        };
    }

    // =============================================================================================
    // Refunds
    // =============================================================================================

    /// <summary>
    /// Submits a refund to Razorpay.
    /// </summary>
    /// <remarks>
    /// IT REFUSES TO GUESS AT A PAYMENT ID. A refund is addressed to a payment, and if all we hold
    /// is a payment link the link is resolved first - refunding the wrong payment is not something
    /// that can be undone with an apology.
    /// </remarks>
    public async Task<GatewayRefundResult> RefundAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        MoneyValue amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayReference);

        var credential = ResolveUsableCredential(account);

        if (credential is null)
        {
            return GatewayRefundResult.Failed(
                "GATEWAY_NOT_CONFIGURED", "Razorpay is not configured for this organisation.");
        }

        try
        {
            var client = CreateClient(credential);

            var paymentId = gatewayReference;

            if (gatewayReference.StartsWith("plink_", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await ResolveLinkAsync(client, gatewayReference, cancellationToken);

                if (resolved.PaymentId is null)
                {
                    return GatewayRefundResult.Failed(
                        "NO_PAYMENT_TO_REFUND",
                        "No payment was found against this link, so there is nothing to refund.");
                }

                paymentId = resolved.PaymentId;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"payments/{Uri.EscapeDataString(paymentId)}/refund")
            {
                Content = JsonContent.Create(
                    new RefundPayload
                    {
                        Amount = ToMinorUnits(amount),
                        Notes = new Dictionary<string, string> { ["idempotency_key"] = idempotencyKey }
                    },
                    options: JsonOptions)
            };

            // Razorpay DOES honour this one on refunds, and it is what stops a retried request
            // from sending the donor's money back twice.
            request.Headers.TryAddWithoutValidation("X-Razorpay-Idempotency-Key", idempotencyKey);

            using var response = await client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = ReadError(body);

                logger.LogWarning(
                    "Razorpay refused a refund on {PaymentId}: {StatusCode} {Body}",
                    paymentId,
                    (int)response.StatusCode,
                    Truncate(body));

                return GatewayRefundResult.Failed(
                    $"RAZORPAY_{(int)response.StatusCode}",
                    error ?? "Razorpay refused the refund.");
            }

            var result = JsonSerializer.Deserialize<RefundResponse>(body, JsonOptions);

            return string.IsNullOrWhiteSpace(result?.Id)
                ? GatewayRefundResult.Failed(
                    "RAZORPAY_BAD_RESPONSE", "Razorpay returned no refund reference.")
                : GatewayRefundResult.Ok(result.Id);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception, "Could not reach Razorpay to refund {GatewayReference}.", gatewayReference);

            return GatewayRefundResult.Failed(
                "GATEWAY_UNREACHABLE", "We could not reach Razorpay to submit the refund.");
        }
    }

    // =============================================================================================
    // Webhooks
    // =============================================================================================

    /// <summary>
    /// Verifies a Razorpay webhook signature.
    /// </summary>
    /// <remarks>
    /// HMAC-SHA256 OF THE RAW BODY, hex-encoded, keyed on the webhook secret, compared in constant
    /// time. The raw body matters: re-serialising the JSON before hashing changes the bytes and
    /// every signature fails, which is the commonest way this check is broken in practice.
    ///
    /// A MISSING SECRET FAILS CLOSED. Anybody can post to a webhook URL; without the secret there
    /// is no way to tell a provider's callback from a fabricated payment, so the event is queued
    /// unverified rather than acted on.
    /// </remarks>
    public bool VerifyWebhookSignature(
        PaymentGatewayAccount account, string payload, string signatureHeader)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var credential = credentials.Resolve(account);

        if (credential is null || string.IsNullOrWhiteSpace(credential.WebhookSecret))
        {
            logger.LogWarning(
                "A Razorpay webhook arrived for merchant {MerchantId} but no webhook secret is "
                + "configured, so it cannot be verified and will not be acted on.",
                account.MerchantId);

            return false;
        }

        try
        {
            var expected = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(credential.WebhookSecret),
                Encoding.UTF8.GetBytes(payload));

            var provided = Convert.FromHexString(signatureHeader.Trim());

            return provided.Length == expected.Length
                   && CryptographicOperations.FixedTimeEquals(expected, provided);
        }
        catch (FormatException)
        {
            logger.LogWarning(
                "A Razorpay webhook for merchant {MerchantId} carried a signature that is not "
                + "hexadecimal. It will not be acted on.",
                account.MerchantId);

            return false;
        }
    }

    /// <summary>
    /// Reads a Razorpay webhook envelope.
    /// </summary>
    /// <remarks>
    /// RAZORPAY PUTS NO EVENT ID IN THE BODY - it is in the <c>x-razorpay-event-id</c> header,
    /// which this interface does not receive. So the id is derived from the event name and the
    /// entity it concerns, which is stable across redeliveries of the SAME event and different
    /// between different ones. That is exactly the property the de-duplication needs: Razorpay
    /// retries a webhook until it is acknowledged, and each retry has to land on the same row.
    ///
    /// THE ENTITY IS WHICHEVER ONE THE EVENT IS ABOUT. A payment event carries a payment; a refund
    /// event carries both a refund and the payment it belongs to, and the PAYMENT is what our
    /// records are keyed by.
    /// </remarks>
    public GatewayWebhookEvent? ParseWebhook(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var body = JsonSerializer.Deserialize<WebhookEnvelope>(payload, JsonOptions);

            // NOT A RAZORPAY BODY. Returning null rather than a half-read event is what lets the
            // router try the next adapter instead of recording nonsense.
            if (body is null
                || string.IsNullOrWhiteSpace(body.Event)
                || !string.Equals(body.Entity, "event", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var payment = body.Payload?.Payment?.Entity;
            var refund = body.Payload?.Refund?.Entity;
            var dispute = body.Payload?.Dispute?.Entity;

            var reference = payment?.Id ?? refund?.PaymentId ?? dispute?.PaymentId;
            var amountMinor = refund?.Amount ?? dispute?.Amount ?? payment?.Amount;
            var currency = refund?.Currency ?? dispute?.Currency ?? payment?.Currency;

            var occurredAt = body.CreatedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(body.CreatedAt.Value)
                : DateTimeOffset.UtcNow;

            var entityId = refund?.Id ?? dispute?.Id ?? payment?.Id ?? body.CreatedAt?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

            return new GatewayWebhookEvent(
                $"{body.Event}:{entityId}",
                MapEventType(body.Event),
                reference,
                amountMinor is null ? null : amountMinor.Value / 100m,
                currency,
                occurredAt);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception, "A Razorpay webhook payload could not be parsed. It will be queued as-is.");

            return null;
        }
    }

    // =============================================================================================
    // Mapping
    // =============================================================================================

    /// <summary>Razorpay's payment status to ours.</summary>
    private static PaymentAttemptStatus MapStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "captured" => PaymentAttemptStatus.Succeeded,
            "authorized" => PaymentAttemptStatus.Authorised,
            "failed" => PaymentAttemptStatus.Failed,
            "refunded" => PaymentAttemptStatus.Succeeded,
            "created" => PaymentAttemptStatus.Initiated,
            _ => PaymentAttemptStatus.Pending
        };

    /// <summary>Razorpay's event names to ours.</summary>
    /// <summary>
    /// Where Razorpay sends the donor after they pay.
    ///
    /// The account's own <c>ReturnUrl</c> wins when it is set. Otherwise the configured client
    /// base URL and payment-result path are joined - which is what makes the return trip work
    /// out of the box instead of depending on a column nobody fills in.
    ///
    /// NULL WHEN NEITHER IS CONFIGURED, because Razorpay refuses a malformed callback_url and
    /// would reject the whole payment link. No callback is a worse donor experience; a rejected
    /// link is no donation at all.
    /// </summary>
    private string? ResolveCallbackUrl(PaymentGatewayAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.ReturnUrl))
        {
            return account.ReturnUrl;
        }

        if (string.IsNullOrWhiteSpace(_client.BaseUrl)
            || string.IsNullOrWhiteSpace(_client.PaymentResultPath))
        {
            return null;
        }

        return $"{_client.BaseUrl.TrimEnd('/')}/{_client.PaymentResultPath.TrimStart('/')}";
    }

    private static PaymentEventType MapEventType(string? eventName) =>
        eventName?.Trim().ToLowerInvariant() switch
        {
            "payment.captured" => PaymentEventType.Captured,
            "payment.authorized" => PaymentEventType.Authorised,
            "payment.failed" => PaymentEventType.Failed,

            // A paid link is the same fact as a captured payment, and both arrive. The
            // de-duplication is by event id, so recording both is harmless and losing one is not.
            "payment_link.paid" => PaymentEventType.Captured,
            "payment_link.expired" => PaymentEventType.Expired,
            "payment_link.cancelled" => PaymentEventType.Cancelled,

            "refund.processed" or "refund.created" => PaymentEventType.Refunded,
            "refund.partial" => PaymentEventType.PartiallyRefunded,

            "payment.dispute.created" => PaymentEventType.ChargebackOpened,
            "payment.dispute.won" => PaymentEventType.ChargebackWon,
            "payment.dispute.lost" or "payment.dispute.closed" => PaymentEventType.ChargebackLost,

            "settlement.processed" => PaymentEventType.Settled,

            _ => PaymentEventType.Unknown
        };

    /// <summary>Razorpay's method names to ours.</summary>
    private static PaymentMethodType? MapMethod(string? method) =>
        method?.Trim().ToLowerInvariant() switch
        {
            "card" => PaymentMethodType.Card,
            "netbanking" => PaymentMethodType.NetBanking,
            "upi" => PaymentMethodType.Upi,
            "wallet" => PaymentMethodType.Wallet,
            "bank_transfer" => PaymentMethodType.BankTransfer,
            "emandate" or "nach" => PaymentMethodType.DirectDebit,
            null or "" => null,
            _ => PaymentMethodType.Other
        };

    /// <summary>One Razorpay payment, in our vocabulary.</summary>
    private static GatewayVerificationResult Describe(PaymentResponse payment)
    {
        var currency = string.IsNullOrWhiteSpace(payment.Currency) ? "INR" : payment.Currency;
        var status = MapStatus(payment.Status);

        // ONLY A CAPTURED PAYMENT HAS BEEN COLLECTED. An authorised one is a hold on the donor's
        // card and no money has moved, so reporting an amount against it would put income in the
        // register that the organisation cannot spend.
        var captured = status == PaymentAttemptStatus.Succeeded && payment.Amount.HasValue
            ? MoneyValue.Create(payment.Amount.Value / 100m, currency)
            : null;

        var fee = payment.Fee.HasValue && payment.Fee.Value > 0
            ? MoneyValue.Create(payment.Fee.Value / 100m, currency)
            : null;

        return new GatewayVerificationResult(
            status,
            captured,
            fee,
            MapMethod(payment.Method),
            MaskedInstrument(payment),
            payment.CreatedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(payment.CreatedAt.Value)
                : null,
            payment.ErrorCode,
            payment.ErrorDescription);
    }

    /// <summary>
    /// What the donor would recognise, and nothing more.
    ///
    /// The last four digits of a card, a masked VPA, or the bank's name. Never a full instrument:
    /// this string is rendered on screens and written into exports.
    /// </summary>
    private static string? MaskedInstrument(PaymentResponse payment)
    {
        if (!string.IsNullOrWhiteSpace(payment.Card?.Last4))
        {
            return $"•••• {payment.Card.Last4}";
        }

        if (!string.IsNullOrWhiteSpace(payment.Vpa))
        {
            var at = payment.Vpa.IndexOf('@', StringComparison.Ordinal);
            return at > 1 ? $"{payment.Vpa[0]}•••{payment.Vpa[at..]}" : "•••";
        }

        return string.IsNullOrWhiteSpace(payment.Bank) ? payment.Wallet : payment.Bank;
    }

    private static GatewayVerificationResult Unknown(string code, string message) =>
        new(PaymentAttemptStatus.TimedOut, null, null, null, null, null, code, message);

    // =============================================================================================
    // Plumbing
    // =============================================================================================

    /// <summary>
    /// This organisation's Razorpay credentials, or null when they are not really there.
    ///
    /// WHY THIS IS NOT JUST <c>credentials.Resolve</c>. The resolver is provider-agnostic: it
    /// hands back whatever string the configuration holds under <c>ApiKey</c> and cannot know
    /// what a usable one looks like. Razorpay's is a PAIR - <c>key_id:key_secret</c> - and a
    /// deployment supplies the two halves as separate environment variables, so before they are
    /// filled in the composed value arrives as <c>":"</c>, or as <c>"rzp_test_abc:"</c> when only
    /// the id was pasted. Both are non-empty, both satisfy the resolver, and both then reach
    /// Razorpay as an empty Basic credential and come back 401.
    ///
    /// A 401 IS THE WRONG ANSWER TO GIVE A DONOR. It surfaces as "the payment provider could not
    /// be reached", which sends whoever is configuring the deployment looking at networking
    /// rather than at the blank line in their .env. An unconfigured gateway should say it is
    /// unconfigured, which is what every caller of this already does with a null.
    ///
    /// THE WEBHOOK SIGNATURE PATH DELIBERATELY DOES NOT USE THIS. It needs only the webhook
    /// secret, and an incomplete API key pair is no reason to stop verifying callbacks.
    /// </summary>
    private GatewayCredential? ResolveUsableCredential(PaymentGatewayAccount account)
    {
        var credential = credentials.Resolve(account);

        if (credential is null)
        {
            return null;
        }

        // No colon at all is a pre-encoded credential - see CreateClient - and is left alone.
        var separator = credential.ApiKey.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            return credential;
        }

        var keyId = credential.ApiKey[..separator];
        var keySecret = credential.ApiKey[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
        {
            // Logged WITHOUT the value, for the same reason the resolver does not log it either.
            logger.LogWarning(
                "The Razorpay credential at PaymentGateways:{Reference} is incomplete - Basic "
                + "authentication needs both a key id and a key secret. Payments for merchant "
                + "{MerchantId} will be refused until both halves are deployed.",
                account.ApiKeyReference,
                account.MerchantId);

            return null;
        }

        return credential;
    }

    /// <summary>
    /// A client authenticated as this organisation's merchant.
    ///
    /// RAZORPAY USES HTTP BASIC with the key id as the username and the key secret as the
    /// password. The configured <c>ApiKey</c> therefore holds <c>key_id:key_secret</c>; a value
    /// with no colon is treated as a pre-encoded credential and passed through, which is what a
    /// deployment storing the base64 pair directly would hold.
    /// </summary>
    private HttpClient CreateClient(GatewayCredential credential)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var baseUrl = string.IsNullOrWhiteSpace(credential.BaseUrl) ? DefaultBaseUrl : credential.BaseUrl;

        client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

        client.Timeout = TimeSpan.FromSeconds(
            _settings.GatewayTimeoutSeconds > 0 ? _settings.GatewayTimeoutSeconds : 30);

        var parameter = credential.ApiKey.Contains(':', StringComparison.Ordinal)
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.ApiKey))
            : credential.ApiKey;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", parameter);

        return client;
    }

    /// <summary>
    /// What the donor sees on Razorpay's page and on their statement.
    ///
    /// THE INTENT REFERENCE AND NOTHING ELSE. It is what our support desk asks for, what appears
    /// on the receipt, and the one string that ties the two systems together - and unlike a
    /// campaign name it cannot accidentally carry a donor's own words onto a bank statement.
    /// </summary>
    private static string Describe(DonationIntent intent) =>
        Trim($"Donation {intent.IntentReference}", 255);

    /// <summary>
    /// A contact Razorpay will accept, or nothing at all.
    ///
    /// It wants an E.164-ish string. Anything that cannot be made into one is dropped rather than
    /// sent, because Razorpay rejects the WHOLE request on a malformed contact - which would turn
    /// a donor's stray typing into "the payment link could not be created".
    /// </summary>
    private static string? NormaliseContact(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        var digits = new string([.. mobile.Where(char.IsDigit)]);

        if (digits.Length is < 8 or > 15)
        {
            return null;
        }

        return mobile.TrimStart().StartsWith('+') ? $"+{digits}" : digits;
    }

    private static long ToMinorUnits(MoneyValue money) =>
        (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);

    private static string Trim(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maximum ? value : value[..maximum];

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    /// <summary>Razorpay's own words for a failure, when it gave any.</summary>
    private static string? ReadError(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions);
            return error?.Error?.Description;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // =============================================================================================
    // Razorpay's wire shapes
    // =============================================================================================

    private sealed class CreateOrderPayload
    {
        public long Amount { get; init; }
        public string Currency { get; init; } = "INR";
        public string? Receipt { get; init; }

        /// <summary>Serialises to Razorpay's payment_capture; true captures on authorisation.</summary>
        public bool PaymentCapture { get; init; }

        public Dictionary<string, string>? Notes { get; init; }
    }

    private sealed class OrderResponse
    {
        public string? Id { get; init; }
        public long Amount { get; init; }
        public string? Currency { get; init; }
        public string? Status { get; init; }
    }

    private sealed class CreateLinkPayload
    {
        public long Amount { get; init; }
        public string Currency { get; init; } = "INR";
        public bool AcceptPartial { get; init; }
        public string Description { get; init; } = string.Empty;
        public string ReferenceId { get; init; } = string.Empty;
        public CustomerPayload? Customer { get; init; }
        public NotifyPayload? Notify { get; init; }
        public bool ReminderEnable { get; init; }
        public string? CallbackUrl { get; init; }
        public string? CallbackMethod { get; init; }
        public long? ExpireBy { get; init; }
        public IDictionary<string, string>? Notes { get; init; }
    }

    private sealed class CustomerPayload
    {
        public string? Name { get; init; }
        public string? Email { get; init; }
        public string? Contact { get; init; }
    }

    private sealed class NotifyPayload
    {
        public bool Sms { get; init; }
        public bool Email { get; init; }
    }

    private sealed class RefundPayload
    {
        public long Amount { get; init; }
        public IDictionary<string, string>? Notes { get; init; }
    }

    private sealed class PaymentLinkResponse
    {
        public string? Id { get; init; }
        public string? ShortUrl { get; init; }
        public string? Status { get; init; }
        public long? ExpireBy { get; init; }
        public IReadOnlyList<LinkPayment>? Payments { get; init; }
    }

    private sealed class LinkPayment
    {
        public string? PaymentId { get; init; }
        public string? Status { get; init; }
        public long? Amount { get; init; }
        public long? CreatedAt { get; init; }
    }

    private sealed class PaymentResponse
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
        public long? Amount { get; init; }
        public string? Currency { get; init; }
        public long? Fee { get; init; }
        public string? Method { get; init; }
        public string? Bank { get; init; }
        public string? Wallet { get; init; }
        public string? Vpa { get; init; }
        public CardResponse? Card { get; init; }
        public long? CreatedAt { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorDescription { get; init; }
    }

    private sealed class CardResponse
    {
        public string? Last4 { get; init; }
        public string? Network { get; init; }
    }

    private sealed class RefundResponse
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
    }

    private sealed class ErrorEnvelope
    {
        public ErrorBody? Error { get; init; }
    }

    private sealed class ErrorBody
    {
        public string? Code { get; init; }
        public string? Description { get; init; }
        public string? Reason { get; init; }
    }

    private sealed class WebhookEnvelope
    {
        public string? Entity { get; init; }
        public string? Event { get; init; }
        public long? CreatedAt { get; init; }
        public WebhookPayloadBody? Payload { get; init; }
    }

    private sealed class WebhookPayloadBody
    {
        public WebhookWrapper<WebhookPaymentEntity>? Payment { get; init; }
        public WebhookWrapper<WebhookRefundEntity>? Refund { get; init; }
        public WebhookWrapper<WebhookDisputeEntity>? Dispute { get; init; }
    }

    private sealed class WebhookWrapper<T>
    {
        public T? Entity { get; init; }
    }

    private sealed class WebhookPaymentEntity
    {
        public string? Id { get; init; }
        public long? Amount { get; init; }
        public string? Currency { get; init; }
        public string? Status { get; init; }
    }

    private sealed class WebhookRefundEntity
    {
        public string? Id { get; init; }
        public string? PaymentId { get; init; }
        public long? Amount { get; init; }
        public string? Currency { get; init; }
    }

    private sealed class WebhookDisputeEntity
    {
        public string? Id { get; init; }
        public string? PaymentId { get; init; }
        public long? Amount { get; init; }
        public string? Currency { get; init; }
    }
}
