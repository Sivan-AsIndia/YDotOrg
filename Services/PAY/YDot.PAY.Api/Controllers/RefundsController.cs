using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Refunds.Commands.ManageRefund;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Refunds.Queries;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The refund register - SCR-PAY-006.
///
/// RAISING A REFUND LIVES ON THE DONATION CONTROLLER, because that is where an operator is
/// standing when they decide one is needed. Everything after that - approving, rejecting,
/// tracking - lives here, on the case.
///
/// THE SEGREGATION OF DUTIES IS THE POINT OF THIS CONTROLLER. Approve and reject are refused to
/// the person who raised the case, WHATEVER PERMISSIONS THEY HOLD. That is a rule about who,
/// relative to this record - which no permission code can express - so it is enforced in the
/// handler and reflected in the permitted-action list, meaning the screen never draws a button
/// that will answer 409.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/refunds")]
[Produces("application/json")]
public sealed class RefundsController(
    RefundQueryHandler queries, RefundCommandHandler refunds) : ApiControllerBase
{
    /// <summary>The refund register.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.RefundsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<RefundCaseListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] RefundSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchRefundsQuery(filter), cancellationToken));

    /// <summary>The CSV export.</summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.RefundsExport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] RefundSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportRefundsQuery(filter), cancellationToken));

    /// <summary>
    /// One refund case in full.
    ///
    /// The permitted-action list on the response is computed for THIS caller: somebody looking at
    /// a case they raised themselves gets View and Export, and no Approve.
    /// </summary>
    [HttpGet("{id:guid}", Name = nameof(GetRefundAsync))]
    [HasPermission(PermissionCodes.RefundsView)]
    [ProducesResponseType(typeof(ApiResponse<RefundCaseDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRefundAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetRefundQuery(id), cancellationToken));

    /// <summary>
    /// Approves a refund, which is what actually sends money back.
    ///
    /// REFUSED TO THE PERSON WHO RAISED IT. Money leaving the organisation needs two people, and
    /// this is where that is enforced.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.RefundsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(
        Guid id, [FromBody] DecideRefundRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await refunds.HandleAsync(new ApproveRefundCommand(id, request), cancellationToken),
            "Refund approved.");

    /// <summary>
    /// Rejects a refund.
    ///
    /// A REASON IS MANDATORY. Somebody asked for this and is entitled to an answer, and "rejected"
    /// with nothing beside it is the kind of record that generates a second identical request a
    /// week later.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.RefundsReject)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectAsync(
        Guid id, [FromBody] RejectRefundRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await refunds.HandleAsync(new RejectRefundCommand(id, request), cancellationToken),
            "Refund rejected.");
}
