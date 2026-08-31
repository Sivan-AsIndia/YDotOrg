using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Payments.Commands.ProcessPayment;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Application.Features.Payments.Queries;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// Payment verification, the gateway event queue and safe retry - SCR-PAY-002, SCR-PAY-003 and
/// SCR-PAY-007.
///
/// THIS IS THE OPERATIONAL HEART OF THE MODULE. Everything here exists because payments do not
/// always resolve cleanly: a webhook that arrived twice, an attempt that timed out, a donor who
/// says they paid and a register that says they did not.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/payments")]
[Produces("application/json")]
public sealed class PaymentsController(
    PaymentProcessingCommandHandler payments, PaymentEventQueryHandler events) : ApiControllerBase
{
    /// <summary>
    /// Asks the gateway what actually happened to an attempt.
    ///
    /// THE STAFF VERSION, taking an attempt id. The donor's version takes an intent reference and
    /// lives on the public controller; both reach the same handler and neither ever retries the
    /// payment, because a retry disguised as a check is how somebody gets charged twice.
    /// </summary>
    [HttpPost("verify")]
    [HasPermission(PermissionCodes.PaymentsVerify)]
    [ProducesResponseType(typeof(ApiResponse<PaymentVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyAsync(
        [FromBody] VerifyPaymentRequest request, CancellationToken cancellationToken) =>
        FromResult(await payments.HandleAsync(new VerifyPaymentCommand(request), cancellationToken));

    /// <summary>
    /// Safe retry - section 23.
    ///
    /// NOT A PLAIN RETRY. The handler verifies the previous attempt with the gateway FIRST and
    /// refuses if it actually succeeded, which is the whole difference between helping a donor
    /// whose card was declined and charging one who has already paid. The outcome says which of
    /// the four things happened - Retried, AlreadyPaid, StillPending or Refused - so the operator
    /// can tell the donor something true.
    /// </summary>
    [HttpPost("intents/{intentId:guid}/safe-retry")]
    [HasPermission(PermissionCodes.PaymentsSafeRetry)]
    [ProducesResponseType(typeof(ApiResponse<SafeRetryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SafeRetryAsync(
        Guid intentId, [FromBody] SafeRetryRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await payments.HandleAsync(new SafeRetryCommand(intentId, request), cancellationToken));

    // =====================================================================================
    // The gateway event queue - SCR-PAY-003
    // =====================================================================================

    /// <summary>The event queue.</summary>
    [HttpGet("events")]
    [HasPermission(PermissionCodes.PaymentsViewEvents)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<PaymentEventListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchEventsAsync(
        [FromQuery] PaymentEventSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await events.HandleAsync(new SearchPaymentEventsQuery(filter), cancellationToken));

    /// <summary>
    /// One queued event, with its verbatim payload.
    ///
    /// THE RAW PAYLOAD IS WITHHELD unless the caller holds the events permission - a gateway body
    /// can contain donor contact details that the masked views deliberately hide, and it has no
    /// fixed shape to mask reliably.
    /// </summary>
    [HttpGet("events/{id:guid}")]
    [HasPermission(PermissionCodes.PaymentsViewEvents)]
    [ProducesResponseType(typeof(ApiResponse<PaymentEventDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await events.HandleAsync(new GetPaymentEventQuery(id), cancellationToken));

    /// <summary>
    /// Re-runs a failed event through the processor.
    ///
    /// IT REFUSES AN EVENT THAT ALREADY APPLIED. Reprocessing one of those would record the
    /// donation a second time, which is the exact failure the queue exists to prevent.
    /// </summary>
    [HttpPost("events/{id:guid}/reprocess")]
    [HasPermission(PermissionCodes.PaymentsReprocessEvent)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReprocessEventAsync(
        Guid id, [FromBody] ReprocessPaymentEventRequest request, CancellationToken cancellationToken)
    {
        // The expected version is carried on the body for optimistic concurrency; the handler
        // itself takes only the id, because applying an event is idempotent by its unique
        // gateway event id rather than by version.
        _ = request;

        return FromResult(
            await payments.HandleAsync(new ApplyPaymentEventCommand(id), cancellationToken),
            "Event reprocessed.");
    }

    /// <summary>
    /// Marks an event as needing no action.
    ///
    /// THE EVENT IS KEPT, NOT DELETED. Dismissing says "a person looked at this and decided
    /// nothing was needed", which is a more useful statement than the row not existing - and it
    /// records who decided.
    /// </summary>
    [HttpPost("events/{id:guid}/dismiss")]
    [HasPermission(PermissionCodes.PaymentsDismissEvent)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissEventAsync(
        Guid id, [FromBody] DismissPaymentEventRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await events.HandleAsync(new DismissPaymentEventCommand(id, request), cancellationToken),
            "Event dismissed.");
}
