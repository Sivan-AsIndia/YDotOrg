using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Features.Payments.Commands.ProcessPayment;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// Where payment providers post their callbacks.
///
/// THIS IS THE MOST EXPOSED SURFACE IN THE PLATFORM: a public, unauthenticated POST endpoint that
/// can cause a donation to be recorded. Four decisions defend it, and each one is doing real
/// work.
///
///   1. THE SIGNATURE IS THE ONLY AUTHENTICATION. Anybody can post here. The HMAC over the raw
///      body, checked against the merchant's webhook secret, is what distinguishes the provider
///      from somebody who read our API documentation. An event that fails is STORED and never
///      acted on - keeping it is what makes an attempted forgery visible afterwards.
///   2. THE RAW BODY IS READ BEFORE MODEL BINDING. The signature is computed over the exact
///      bytes the provider sent; a body that has been through a JSON deserialiser and back has
///      different whitespace and different key order, and would never verify. That is why this
///      controller reads the stream itself instead of taking a typed parameter.
///   3. IT ALWAYS ANSWERS 200. A non-2xx makes providers retry for days and eventually disable
///      the endpoint, and there is nothing a provider can usefully do about our processing
///      failure anyway. The event is queued; a person works the queue. The one thing that must
///      never happen is a provider giving up on a genuine capture because we answered 500.
///   4. NOTHING IS PROCESSED INLINE. The handler stores the event and returns. Applying it -
///      recording the donation, issuing the receipt - happens against the stored row, so a
///      redelivery of the same event id is recognised as the duplicate it is.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks")]
public sealed class WebhooksController(
    PaymentProcessingCommandHandler payments, ILogger<WebhooksController> logger)
    : ControllerBase
{
    /// <summary>
    /// The headers different providers use for their signature.
    ///
    /// A LIST RATHER THAN ONE NAME because every provider chose differently, and the alternative
    /// is a separate endpoint per provider that is identical apart from one string.
    /// </summary>
    private static readonly string[] SignatureHeaderNames =
    [
        "X-Signature",
        "X-Webhook-Signature",
        "X-Razorpay-Signature",
        "Stripe-Signature",
        "X-PayU-Signature",
        "X-Cashfree-Signature"
    ];

    /// <summary>The largest body accepted, in bytes.</summary>
    private const int MaximumPayloadBytes = 512 * 1024;

    /// <summary>
    /// Receives one provider callback.
    ///
    /// <paramref name="gatewayName"/> IS IN THE ROUTE, not the body, so the signature is checked
    /// against the right secret before anything in the body is trusted. Reading the provider name
    /// out of an unverified payload would let a forger choose which secret we check against.
    /// </summary>
    [HttpPost("{gatewayName}")]
    [Consumes("application/json", "text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReceiveAsync(
        string gatewayName, CancellationToken cancellationToken)
    {
        // Read the EXACT bytes. See point 2 in the class comment.
        Request.EnableBuffering();

        string payload;

        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        if (payload.Length > MaximumPayloadBytes)
        {
            // Refused BEFORE the signature check, because verifying an unbounded body is work an
            // unauthenticated caller can ask for repeatedly. A real provider callback is a few
            // kilobytes.
            logger.LogWarning(
                "Rejected an oversized webhook body from {GatewayName} ({Length} bytes).",
                gatewayName,
                payload.Length);

            return Ok(new { received = false, reason = "payload too large" });
        }

        var signature = SignatureHeaderNames
            .Select(name => Request.Headers[name].FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var result = await payments.HandleAsync(
            new IngestGatewayWebhookCommand(gatewayName, payload, signature), cancellationToken);

        if (result.IsFailure)
        {
            // LOGGED, THEN ANSWERED 200. See point 3: a retry storm helps nobody, and the failure
            // is already recorded as an audit row for somebody to look at.
            logger.LogWarning(
                "A webhook from {GatewayName} could not be ingested: {Code} {Message}",
                gatewayName,
                result.Error!.Code,
                result.Error.Message);

            return Ok(new { received = true, accepted = false });
        }

        // The stored event's id is echoed so a provider's own logs and ours can be lined up when
        // somebody is working out what happened to a payment.
        return Ok(new { received = true, accepted = true, eventId = result.Value });
    }
}
