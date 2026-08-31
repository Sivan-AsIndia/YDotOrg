using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.AssignmentBoard.Commands.RouteLeads;
using YDots.DON.Application.Features.AssignmentBoard.DTOs;
using YDots.DON.Application.Features.AssignmentBoard.Queries.GetAssignmentBoard;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-006 Assignment board. Balance ownership by team, language, workload and SLA.
/// Route from the developer contract: /api/v1/donors/assignment-board.
/// </summary>
[Route("api/v1/donors/assignment-board")]
[Authorize]
public sealed class AssignmentBoardController : ApiControllerBase
{
    /// <summary>GET the board: routable leads on one side, owners and their workload on the other.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.AssignmentBoardView)]
    [ProducesResponseType(typeof(ApiResponse<AssignmentBoardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBoard(
        [FromQuery] LeadSearchFilter filter,
        [FromServices] AssignmentBoardQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetAssignmentBoardQuery(filter), cancellationToken));

    /// <summary>GET the append-only ownership trail for one lead. This is "Inspect history".</summary>
    [HttpGet("{leadId:guid}/history")]
    [HasPermission(PermissionCodes.AssignmentBoardView)]
    [ProducesResponseType(typeof(ApiResponse<AssignmentBoardLeadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(
        Guid leadId,
        [FromServices] AssignmentBoardQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetAssignmentHistoryQuery(leadId), cancellationToken));

    /// <summary>POST assign. For a lead that has no owner yet.</summary>
    [HttpPost("assign")]
    [HasPermission(PermissionCodes.AssignmentBoardAssign)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        [FromBody] AssignmentRequest request,
        [FromServices] AssignmentBoardCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new AssignFromBoardCommand(request), cancellationToken),
            "The lead was assigned.");

    /// <summary>POST reassign. For a lead that already has an owner.</summary>
    [HttpPost("reassign")]
    [HasPermission(PermissionCodes.AssignmentBoardReassign)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reassign(
        [FromBody] AssignmentRequest request,
        [FromServices] AssignmentBoardCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new ReassignFromBoardCommand(request), cancellationToken),
            "The lead was reassigned.");

    /// <summary>
    /// POST bulk route. Every lead is reported separately: routed or skipped with a reason.
    /// A partial result is still a 200 — the per-record outcome is the answer, not the status.
    /// </summary>
    [HttpPost("bulk-route")]
    [HasPermission(PermissionCodes.AssignmentBoardBulkRoute)]
    [ProducesResponseType(typeof(ApiResponse<BulkRouteResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BulkRoute(
        [FromBody] BulkRouteRequest request,
        [FromServices] AssignmentBoardCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new BulkRouteCommand(request), cancellationToken));
}
