using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Donations.Commands.ManageIntent;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Queries;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The staff view of donation intents - SCR-PAY-001 - and the payment support queue, section 23.
///
/// THE SAME RECORDS AS <see cref="PublicDonationsController"/>, THROUGH A DIFFERENT DOOR. There
/// the caller is a donor holding one unguessable reference; here the caller holds a token, a
/// permission and an Organisation, and can see every intent that Organisation owns. Keeping the
/// two apart means an endpoint's authorization is a fact about the endpoint rather than the
/// result of a branch inside it.
///
/// <c>ActiveUserOnly</c> ON THE CLASS blocks a suspended account that still holds a live token -
/// a permission check alone would not, because the claims in the token were true when it was
/// issued.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/donation-intents")]
[Produces("application/json")]
public sealed class DonationIntentsController(
    DonationIntentCommandHandler intents, DonationQueryHandler queries) : ApiControllerBase
{
    /// <summary>The intent register.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.IntentsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<DonationIntentListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] DonationIntentSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchDonationIntentsQuery(filter), cancellationToken));

    /// <summary>
    /// One intent in full, with its whole attempt history - section 24.
    ///
    /// VIEWING UNMASKED DONOR DETAIL IS AUDITED by the handler. Holding the permission is not the
    /// same as using it, and "who looked at this donor's tax identifier?" is a question a
    /// data-protection review asks.
    /// </summary>
    [HttpGet("{id:guid}", Name = nameof(GetDonationIntentAsync))]
    [HasPermission(PermissionCodes.IntentsView)]
    [ProducesResponseType(typeof(ApiResponse<DonationIntentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDonationIntentAsync(
        Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetDonationIntentQuery(id), cancellationToken));

    /// <summary>
    /// Sends the payment link again.
    ///
    /// The commonest support action there is: a donor who lost the e-mail, or whose link expired
    /// before they got to it. It reuses the intent rather than creating a second one, so the
    /// donor cannot end up holding two live links for one gift.
    /// </summary>
    [HttpPost("{id:guid}/resend-link")]
    [HasPermission(PermissionCodes.IntentsResendLink)]
    [ProducesResponseType(typeof(ApiResponse<PaymentLinkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendLinkAsync(
        Guid id, [FromBody] ResendPaymentLinkBody body, CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(
                new ResendPaymentLinkCommand(id, body.ExpectedVersion), cancellationToken),
            "Payment link re-sent.");

    /// <summary>
    /// Cancels an intent.
    ///
    /// STAFF ONLY, and never available once the intent is paid. Cancelling a paid intent would
    /// leave a donation attached to a cancelled intention, which is a contradiction the reports
    /// cannot express.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(PermissionCodes.IntentsCancel)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(
        Guid id, [FromBody] CancelDonationIntentRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(new CancelDonationIntentCommand(id, request), cancellationToken),
            "Donation intent cancelled.");

    /// <summary>
    /// Section 23: the payment support queue.
    ///
    /// Intents that failed and need a person - which is narrower than "failed": one that failed
    /// once and was then paid needs nobody. What lands here has either exhausted the retry
    /// allowance or has an attempt whose outcome is UNKNOWN, the second being the more urgent
    /// because unknown means the donor may already have been charged.
    /// </summary>
    [HttpGet("support-queue")]
    [HasPermission(PermissionCodes.PaymentsSafeRetry)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<PaymentSupportCaseResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupportQueueAsync(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        FromResult(
            await queries.HandleAsync(new GetPaymentSupportQueueQuery(pagination), cancellationToken));
}

/// <summary>
/// The body of a resend request.
///
/// IT EXISTS ONLY TO CARRY THE EXPECTED VERSION, which is why it is a one-property type rather
/// than a query parameter: optimistic concurrency belongs in the body with the rest of the
/// request, and a version in a query string is one a browser will happily cache and replay.
/// </summary>
public sealed record ResendPaymentLinkBody(long ExpectedVersion);
