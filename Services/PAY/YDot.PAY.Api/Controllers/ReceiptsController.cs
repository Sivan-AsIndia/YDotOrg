using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Receipts.Commands.ManageReceipt;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Receipts.Queries;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The receipt register - SCR-PAY-005 - and the corrections that keep it honest.
///
/// THERE IS NO PUT AND NO DELETE ON THIS CONTROLLER, and their absence is the design. A receipt
/// is a tax document: a donor may have claimed relief on the version they hold, and an auditor
/// may ask to see it years later. So it is never edited and never removed - it is CORRECTED,
/// which issues a NEW version pointing back at what it supersedes, or VOIDED, which leaves the
/// row in place marked void with a reason.
///
/// A VOID DOES NOT FREE ITS NUMBER either. Receipt numbers run in an unbroken per-organisation
/// series, and a gap is exactly what a tax authority reads as a destroyed receipt.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/receipts")]
[Produces("application/json")]
public sealed class ReceiptsController(
    ReceiptQueryHandler queries, ReceiptCommandHandler receipts) : ApiControllerBase
{
    /// <summary>The receipt register.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ReceiptsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<ReceiptListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] ReceiptSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchReceiptsQuery(filter), cancellationToken));

    /// <summary>
    /// The CSV export - the file a finance team files with the year's return.
    /// </summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.ReceiptsExport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] ReceiptSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportReceiptsQuery(filter), cancellationToken));

    /// <summary>
    /// One receipt in full, with its delivery history.
    ///
    /// The DONOR DETAILS ARE THE SNAPSHOT AS ISSUED, not the donor's details today. A receipt is
    /// a statement about a moment, and one that silently updated itself would disagree with the
    /// document in the donor's hand.
    /// </summary>
    [HttpGet("{id:guid}", Name = nameof(GetReceiptAsync))]
    [HasPermission(PermissionCodes.ReceiptsView)]
    [ProducesResponseType(typeof(ApiResponse<ReceiptDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReceiptAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetReceiptQuery(id), cancellationToken));

    /// <summary>
    /// Corrects an issued receipt.
    ///
    /// A CORRECTION IS A NEW VERSION, never an edit, and it takes the NEXT number in the series
    /// rather than reusing the original's. The original stays exactly as issued, because a donor
    /// who claimed relief on version 1 must still be able to show what version 1 said.
    /// </summary>
    [HttpPost("{id:guid}/correct")]
    [HasPermission(PermissionCodes.ReceiptsCorrect)]
    [ProducesResponseType(typeof(ApiResponse<ReceiptDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CorrectAsync(
        Guid id, [FromBody] CorrectReceiptRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await receipts.HandleAsync(new CorrectReceiptCommand(id, request), cancellationToken),
            "Receipt corrected.");

    /// <summary>
    /// Voids a receipt outright, where a correction is not enough.
    ///
    /// A REASON IS MANDATORY, and the database enforces it as well as the handler. "Void, reason
    /// unknown" is precisely the record an auditor challenges.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [HasPermission(PermissionCodes.ReceiptsVoid)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VoidAsync(
        Guid id, [FromBody] VoidReceiptRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await receipts.HandleAsync(new VoidReceiptCommand(id, request), cancellationToken),
            "Receipt voided.");

    /// <summary>
    /// Sends an issued receipt again.
    ///
    /// SENDING TO A DIFFERENT ADDRESS IS AUDITED, because posting somebody's tax document
    /// somewhere other than the address on it is exactly the action that needs justifying later.
    /// </summary>
    [HttpPost("{id:guid}/resend")]
    [HasPermission(PermissionCodes.ReceiptsResend)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendAsync(
        Guid id, [FromBody] ResendReceiptRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await receipts.HandleAsync(new ResendReceiptCommand(id, request), cancellationToken),
            "Receipt re-sent.");
}
