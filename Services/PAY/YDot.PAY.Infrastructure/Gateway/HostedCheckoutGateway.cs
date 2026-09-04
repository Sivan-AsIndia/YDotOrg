using System.Globalization;
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
/// The payment provider adapter: a hosted-checkout gateway spoken to over a documented REST
/// contract, with HMAC-SHA256 webhook signatures.
///
/// WHAT THIS IS AND IS NOT. It is a complete implementation of <see cref="IPaymentGateway"/>
/// against the hosted-checkout shape that Razorpay, Stripe Checkout, PayU and Cashfree all
/// share: create a link, redirect the donor, receive a signed webhook, verify by reference,
/// refund by reference. It is NOT tied to any one of those vendors' SDKs - the endpoints and the
/// field names are configuration, so pointing it at a specific provider is a settings change plus
/// (where a vendor deviates) a subclass overriding <see cref="MapStatus"/> and
/// <see cref="MapEventType"/>.
///
/// THE THREE RULES THAT ARE THIS MODULE'S RATHER THAN ANY PROVIDER'S:
///
///   1. AN UNKNOWN OUTCOME IS NOT A FAILURE. A network timeout means the charge may have
///      succeeded, so it maps to <see cref="PaymentAttemptStatus.TimedOut"/> - which the module
///      resolves by VERIFYING, never by retrying. Reporting it as failed would let a donor be
///      charged twice.
///   2. THE SIGNATURE IS CHECKED IN CONSTANT TIME. A byte-by-byte comparison that returns early
///      leaks, through timing, how much of a forged signature was correct - which is enough to
///      forge one given enough attempts.
///   3. NOTHING IS TRUSTED FROM AN UNSIGNED WEBHOOK. Anybody can post to a webhook URL. An event
///      whose signature fails is stored for investigation and never acted on.
///
/// NO SECRET IS EVER PASSED IN. The credentials are resolved from configuration using the
/// reference held on the account row, so a key never travels through the application layer and
/// never reaches an audit row or a log line.
/// </summary>
public class HostedCheckoutGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentSettings> paymentSettings,
    IGatewayCredentialResolver credentials,
    ILogger<HostedCheckoutGateway> logger) : IPaymentGateway
{
    internal const string HttpClientName = "payment-gateway";

    private readonly PaymentSettings _settings = paymentSettings.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public virtual string GatewayName => "HostedCheckout";

    /// <summary>
    /// Creates a payment link for an intent.
    ///
    /// THE IDEMPOTENCY KEY IS SENT AS A HEADER, which is what makes a repeated call safe: the
    /// provider recognises the repeat and returns the original link rather than opening a second
    /// payment for the same intent. Losing that is how a donor ends up with two links and pays
    /// twice.
    ///
    /// THE EXPIRY COMES FROM THE ACCOUNT, not from this class. A charity that wants links valid
    /// for a day and one that wants fifteen minutes are both reasonable, and the choice belongs
    /// with the organisation rather than the code.
    /// </summary>
    /// <summary>
    /// Declines, because a generic hosted checkout has no in-page form to draw.
    /// </summary>
    /// <remarks>
    /// DECLINING IS THE CORRECT ANSWER, NOT A GAP. An in-page checkout is a provider's own
    /// JavaScript opening over our page against an order it holds; there is no vendor-neutral
    /// shape for that, and inventing one would produce a session no script anywhere knows how to
    /// open. Saying so lets the caller fall back to a payment link - which this adapter does
    /// support, and which every provider behind it can honour.
    /// </remarks>
    public virtual Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Task.FromResult(GatewayCheckoutSession.NotSupported(GatewayName));

    /// <summary>
    /// Always false: this adapter issues no checkout session, so nothing it could be asked to
    /// verify came from it. Fails closed for the same reason every other signature check does.
    /// </summary>
    public virtual bool VerifyCheckoutSignature(
        PaymentGatewayAccount account,
        string orderReference,
        string paymentReference,
        string signature) => false;

    public virtual async Task<GatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayAccount account,
        DonationIntent intent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(intent);

        var credential = credentials.Resolve(account);

        if (credential is null)
        {
            return GatewayLinkResult.Failed(
                "GATEWAY_NOT_CONFIGURED",
                "This organisation's payment gateway credentials are not configured.");
        }

        var validityMinutes = account.PaymentLinkValidityMinutes > 0
            ? account.PaymentLinkValidityMinutes
            : _settings.DefaultPaymentLinkValidityMinutes;

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(validityMinutes);

        var payload = new CreateLinkPayload(
            MerchantId: account.MerchantId,
            Reference: intent.IntentReference,

            // The provider wants minor units - paise, cents. Sending 500.00 where 50000 was
            // meant is the classic hundred-fold error, so the conversion happens once, here.
            AmountMinorUnits: ToMinorUnits(intent.Amount),

            Currency: intent.Amount.CurrencyCode,
            Description: $"Donation {intent.IntentReference}",
            CustomerName: intent.DonorName,
            CustomerEmail: intent.Email,
            CustomerPhone: intent.Mobile,
            ReturnUrl: account.ReturnUrl,
            WebhookUrl: account.WebhookUrl,
            ExpiresAtUtc: expiresAtUtc,
            EnabledMethods: SplitMethods(account.EnabledMethods),
            TestMode: account.IsTestMode);

        try
        {
            var client = CreateClient(credential);

            using var request = new HttpRequestMessage(HttpMethod.Post, "payment-links")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogWarning(
                    "The payment gateway refused a link for intent {IntentReference}: {StatusCode} {Body}",
                    intent.IntentReference,
                    (int)response.StatusCode,
                    Truncate(body));

                return GatewayLinkResult.Failed(
                    $"GATEWAY_{(int)response.StatusCode}",
                    "The payment provider could not create a payment link. Please try again.");
            }

            var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>(
                JsonOptions, cancellationToken);

            if (result is null
                || string.IsNullOrWhiteSpace(result.PaymentUrl)
                || string.IsNullOrWhiteSpace(result.Reference))
            {
                return GatewayLinkResult.Failed(
                    "GATEWAY_BAD_RESPONSE",
                    "The payment provider returned an unusable response. Please try again.");
            }

            return GatewayLinkResult.Ok(result.PaymentUrl, result.Reference, result.ExpiresAtUtc ?? expiresAtUtc);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach the payment gateway to create a link for intent {IntentReference}.",
                intent.IntentReference);

            // NO LINK WAS CREATED, so no money can have moved - this one genuinely is a failure
            // rather than an unknown, and the donor can safely be offered another attempt.
            return GatewayLinkResult.Failed(
                "GATEWAY_UNREACHABLE",
                "We could not reach the payment provider. Please try again in a moment.");
        }
    }

    /// <summary>
    /// Asks the provider what actually happened to an attempt.
    ///
    /// THE MOST IMPORTANT METHOD ON THIS CLASS, and the one whose failure handling matters most.
    /// It is how a timed-out attempt is resolved without guessing.
    ///
    /// A FAILURE TO REACH THE PROVIDER RETURNS <see cref="PaymentAttemptStatus.TimedOut"/>, NOT
    /// FAILED. We asked what happened and did not find out; the attempt's outcome is exactly as
    /// unknown as it was before. Mapping that to Failed would let the module offer a retry on a
    /// payment that may already have succeeded, and the donor would be charged twice.
    /// </summary>
    public virtual async Task<GatewayVerificationResult> VerifyPaymentAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayReference);

        var credential = credentials.Resolve(account);

        if (credential is null)
        {
            return new GatewayVerificationResult(
                PaymentAttemptStatus.TimedOut, null, null, null, null, null,
                "GATEWAY_NOT_CONFIGURED",
                "The payment gateway is not configured, so the outcome could not be checked.");
        }

        try
        {
            var client = CreateClient(credential);

            using var response = await client.GetAsync(
                $"payments/{Uri.EscapeDataString(gatewayReference)}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "The payment gateway could not be queried for {GatewayReference}: {StatusCode}.",
                    gatewayReference,
                    (int)response.StatusCode);

                return new GatewayVerificationResult(
                    PaymentAttemptStatus.TimedOut, null, null, null, null, null,
                    $"GATEWAY_{(int)response.StatusCode}",
                    "The payment provider could not confirm this payment. It will be checked again.");
            }

            var payment = await response.Content.ReadFromJsonAsync<PaymentStatusResponse>(
                JsonOptions, cancellationToken);

            if (payment is null)
            {
                return new GatewayVerificationResult(
                    PaymentAttemptStatus.TimedOut, null, null, null, null, null,
                    "GATEWAY_BAD_RESPONSE",
                    "The payment provider returned an unreadable status.");
            }

            var currency = string.IsNullOrWhiteSpace(payment.Currency) ? "INR" : payment.Currency;

            return new GatewayVerificationResult(
                MapStatus(payment.Status),
                payment.CapturedAmountMinorUnits is null
                    ? null
                    : FromMinorUnits(payment.CapturedAmountMinorUnits.Value, currency),
                payment.FeeMinorUnits is null
                    ? null
                    : FromMinorUnits(payment.FeeMinorUnits.Value, currency),
                MapMethod(payment.Method),
                payment.MaskedInstrument,
                payment.CapturedAtUtc,
                payment.ResultCode,
                payment.Message);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach the payment gateway to verify {GatewayReference}.",
                gatewayReference);

            // See the method comment: unknown, not failed.
            return new GatewayVerificationResult(
                PaymentAttemptStatus.TimedOut, null, null, null, null, null,
                "GATEWAY_UNREACHABLE",
                "We could not confirm this payment with the provider. It will be checked again.");
        }
    }

    /// <summary>
    /// Submits a refund.
    ///
    /// ACCEPTED IS NOT COMPLETED. Providers settle refunds asynchronously, often days later, so
    /// this reports only that the instruction was taken. The module moves the case to Processing
    /// and waits for the webhook that says the money actually went back - telling a donor their
    /// refund is done when the provider has merely queued it is a support call a week later.
    /// </summary>
    public virtual async Task<GatewayRefundResult> RefundAsync(
        PaymentGatewayAccount account,
        string gatewayReference,
        MoneyValue amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayReference);

        var credential = credentials.Resolve(account);

        if (credential is null)
        {
            return GatewayRefundResult.Failed(
                "GATEWAY_NOT_CONFIGURED", "The payment gateway is not configured.");
        }

        try
        {
            var client = CreateClient(credential);

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"payments/{Uri.EscapeDataString(gatewayReference)}/refunds")
            {
                Content = JsonContent.Create(
                    new RefundPayload(ToMinorUnits(amount), amount.CurrencyCode),
                    options: JsonOptions)
            };

            // The same key on a repeat means one refund, not two. On a refund the stakes are the
            // mirror of a payment: a duplicate sends the donor's money back twice.
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogWarning(
                    "The payment gateway refused a refund on {GatewayReference}: {StatusCode} {Body}",
                    gatewayReference,
                    (int)response.StatusCode,
                    Truncate(body));

                return GatewayRefundResult.Failed(
                    $"GATEWAY_{(int)response.StatusCode}",
                    "The payment provider refused the refund.");
            }

            var result = await response.Content.ReadFromJsonAsync<RefundResponse>(
                JsonOptions, cancellationToken);

            return string.IsNullOrWhiteSpace(result?.RefundReference)
                ? GatewayRefundResult.Failed(
                    "GATEWAY_BAD_RESPONSE", "The payment provider returned no refund reference.")
                : GatewayRefundResult.Ok(result.RefundReference);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach the payment gateway to refund {GatewayReference}.",
                gatewayReference);

            return GatewayRefundResult.Failed(
                "GATEWAY_UNREACHABLE", "We could not reach the payment provider to submit the refund.");
        }
    }

    /// <summary>
    /// Verifies a webhook signature.
    ///
    /// ANYBODY CAN POST TO A WEBHOOK URL - it is a public endpoint with a guessable path. The
    /// signature is the only thing that says the provider sent it, and without this check a
    /// stranger could post a "captured" event and have a donation recorded for money that never
    /// arrived.
    ///
    /// <c>CryptographicOperations.FixedTimeEquals</c> RATHER THAN <c>==</c>. A comparison that
    /// returns as soon as two bytes differ takes measurably longer the more of the prefix is
    /// correct, and that difference is enough to reconstruct a valid signature one byte at a time
    /// given enough attempts. Constant time removes the signal entirely.
    /// </summary>
    public virtual bool VerifyWebhookSignature(
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
            // NO SECRET MEANS NO VERIFICATION MEANS NO TRUST. Returning true here "so webhooks
            // work in development" is exactly how an unauthenticated payment endpoint reaches
            // production.
            logger.LogWarning(
                "A webhook arrived for merchant {MerchantId} but no webhook secret is configured, "
                + "so it cannot be verified and will not be acted on.",
                account.MerchantId);

            return false;
        }

        try
        {
            var expected = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(credential.WebhookSecret),
                Encoding.UTF8.GetBytes(payload));

            var provided = ParseSignature(signatureHeader);

            return provided is not null
                   && provided.Length == expected.Length
                   && CryptographicOperations.FixedTimeEquals(expected, provided);
        }
        catch (FormatException)
        {
            // A malformed signature header is a failed verification, not an error worth raising:
            // it is what a probe looks like.
            return false;
        }
    }

    /// <summary>
    /// Turns a raw webhook body into the shape the event queue stores.
    ///
    /// IT RETURNS NULL RATHER THAN THROWING on anything it cannot read. The caller stores the
    /// raw body regardless, so an unparseable payload becomes a queued event a person can look at
    /// - which is far more useful than an exception in a log and a provider retrying for three
    /// days.
    ///
    /// AN UNRECOGNISED EVENT TYPE IS <see cref="PaymentEventType.Unknown"/>, NOT A REJECTION.
    /// Providers add event types without warning, and one we do not understand is still evidence
    /// that something happened to a payment.
    /// </summary>
    public virtual GatewayWebhookEvent? ParseWebhook(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var body = JsonSerializer.Deserialize<WebhookPayload>(payload, JsonOptions);

            if (body is null || string.IsNullOrWhiteSpace(body.EventId))
            {
                return null;
            }

            return new GatewayWebhookEvent(
                body.EventId,
                MapEventType(body.EventType),
                body.PaymentReference,
                body.AmountMinorUnits is null
                    ? null
                    : body.AmountMinorUnits.Value / 100m,
                body.Currency,
                body.OccurredAtUtc ?? DateTimeOffset.UtcNow);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "A webhook payload could not be parsed. It will be queued as-is.");

            return null;
        }
    }

    // =====================================================================================
    // Mapping - the points a specific provider would override
    // =====================================================================================

    /// <summary>
    /// The provider's status word to ours.
    ///
    /// THE DEFAULT IS <see cref="PaymentAttemptStatus.Pending"/>, NOT FAILED. An unrecognised
    /// status is one we do not understand yet, and the safe reading of "do not understand" is
    /// "do not know" - which the module resolves by asking again, rather than by telling a donor
    /// their payment failed when it may not have.
    /// </summary>
    protected virtual PaymentAttemptStatus MapStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "captured" or "succeeded" or "success" or "paid" or "completed" => PaymentAttemptStatus.Succeeded,
            "authorized" or "authorised" => PaymentAttemptStatus.Authorised,
            "failed" or "declined" or "error" => PaymentAttemptStatus.Failed,
            "cancelled" or "canceled" or "abandoned" or "user_dropped" => PaymentAttemptStatus.Abandoned,
            "created" or "initiated" => PaymentAttemptStatus.Initiated,
            "pending" or "processing" or "in_progress" => PaymentAttemptStatus.Pending,
            "timeout" or "timed_out" => PaymentAttemptStatus.TimedOut,
            _ => PaymentAttemptStatus.Pending
        };

    protected virtual PaymentEventType MapEventType(string? eventType) =>
        eventType?.Trim().ToLowerInvariant() switch
        {
            "payment.captured" or "payment.succeeded" or "payment.paid" => PaymentEventType.Captured,
            "payment.authorized" or "payment.authorised" => PaymentEventType.Authorised,
            "payment.failed" or "payment.declined" => PaymentEventType.Failed,
            "payment.cancelled" or "payment.canceled" => PaymentEventType.Cancelled,
            "payment.expired" or "link.expired" => PaymentEventType.Expired,
            "refund.processed" or "refund.completed" => PaymentEventType.Refunded,
            "refund.partial" => PaymentEventType.PartiallyRefunded,
            "dispute.created" or "chargeback.created" => PaymentEventType.ChargebackOpened,
            "dispute.won" or "chargeback.won" => PaymentEventType.ChargebackWon,
            "dispute.lost" or "chargeback.lost" => PaymentEventType.ChargebackLost,
            "settlement.processed" or "payout.settled" => PaymentEventType.Settled,
            _ => PaymentEventType.Unknown
        };

    protected virtual PaymentMethodType? MapMethod(string? method) =>
        method?.Trim().ToLowerInvariant() switch
        {
            "card" or "credit_card" or "debit_card" => PaymentMethodType.Card,
            "netbanking" or "net_banking" => PaymentMethodType.NetBanking,
            "upi" => PaymentMethodType.Upi,
            "wallet" => PaymentMethodType.Wallet,
            "bank_transfer" or "neft" or "rtgs" or "imps" => PaymentMethodType.BankTransfer,
            "emandate" or "nach" or "direct_debit" => PaymentMethodType.DirectDebit,
            null or "" => null,
            _ => PaymentMethodType.Other
        };

    // =====================================================================================
    // Internals
    // =====================================================================================

    private HttpClient CreateClient(GatewayCredential credential)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        client.BaseAddress = new Uri(
            credential.BaseUrl.EndsWith('/') ? credential.BaseUrl : credential.BaseUrl + "/");

        client.Timeout = TimeSpan.FromSeconds(
            _settings.GatewayTimeoutSeconds > 0 ? _settings.GatewayTimeoutSeconds : 30);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.ApiKey);

        return client;
    }

    /// <summary>
    /// Money to the provider's minor units.
    ///
    /// <c>MidpointRounding.AwayFromZero</c>, not banker's rounding. A half-paisa is not a real
    /// amount, but if one ever arises the donor's figure and ours must agree - and away-from-zero
    /// is what every financial statement and every person expects when they see 0.5 rounded.
    /// </summary>
    private static long ToMinorUnits(MoneyValue money) =>
        (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);

    private static MoneyValue FromMinorUnits(long minorUnits, string currencyCode) =>
        MoneyValue.Create(minorUnits / 100m, currencyCode);

    private static IReadOnlyList<string>? SplitMethods(string? commaSeparated) =>
        string.IsNullOrWhiteSpace(commaSeparated)
            ? null
            : [.. commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Reads a signature header.
    ///
    /// Both encodings are accepted because providers disagree: some send lower-case hex, some
    /// base64, and some prefix it with an algorithm name. Rejecting a valid signature over its
    /// encoding would silently stop every webhook.
    /// </summary>
    private static byte[]? ParseSignature(string header)
    {
        var value = header.Trim();

        var separator = value.IndexOf('=', StringComparison.Ordinal);

        if (separator > 0 && separator < value.Length - 1
                          && !value.Contains("==", StringComparison.Ordinal))
        {
            value = value[(separator + 1)..].Trim();
        }

        if (value.Length % 2 == 0
            && value.All(character => Uri.IsHexDigit(character)))
        {
            return Convert.FromHexString(value);
        }

        return Convert.TryFromBase64String(value, new byte[value.Length], out var written)
            ? Convert.FromBase64String(value)[..written]
            : null;
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    // ---- Wire shapes -------------------------------------------------------------------

    private sealed record CreateLinkPayload(
        string MerchantId,
        string Reference,
        long AmountMinorUnits,
        string Currency,
        string Description,
        string CustomerName,
        string CustomerEmail,
        string? CustomerPhone,
        string? ReturnUrl,
        string? WebhookUrl,
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<string>? EnabledMethods,
        bool TestMode);

    private sealed record CreateLinkResponse(
        string? PaymentUrl, string? Reference, DateTimeOffset? ExpiresAtUtc);

    private sealed record PaymentStatusResponse(
        string? Status,
        long? CapturedAmountMinorUnits,
        long? FeeMinorUnits,
        string? Currency,
        string? Method,
        string? MaskedInstrument,
        DateTimeOffset? CapturedAtUtc,
        string? ResultCode,
        string? Message);

    private sealed record RefundPayload(long AmountMinorUnits, string Currency);

    private sealed record RefundResponse(string? RefundReference);

    private sealed record WebhookPayload(
        string? EventId,
        string? EventType,
        string? PaymentReference,
        long? AmountMinorUnits,
        string? Currency,
        DateTimeOffset? OccurredAtUtc);
}
