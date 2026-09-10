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
    private readonly ILogger<AssignmentBoardController> _logger;

    public AssignmentBoardController(ILogger<AssignmentBoardController> logger)
    {
        _logger = logger;
    }

    /// <summary>GET the board: routable leads on one side, owners and their workload on the other.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.AssignmentBoardView)]
    [ProducesResponseType(typeof(ApiResponse<AssignmentBoardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBoard(
        [FromQuery] LeadSearchFilter filter,
        [FromServices] AssignmentBoardQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Assignment board retrieval started.");

        var result = await handler.HandleAsync(new GetAssignmentBoardQuery(filter), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Assignment board retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Assignment board retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>GET the append-only ownership trail for one lead. This is "Inspect history".</summary>
    [HttpGet("{leadId:guid}/history")]
    [HasPermission(PermissionCodes.AssignmentBoardView)]
    [ProducesResponseType(typeof(ApiResponse<AssignmentBoardLeadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(
        Guid leadId,
        [FromServices] AssignmentBoardQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Assignment history retrieval started. LeadId={LeadId}", leadId);

        var result = await handler.HandleAsync(new GetAssignmentHistoryQuery(leadId), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Assignment history retrieval completed successfully. LeadId={LeadId}", leadId);
        }
        else
        {
            _logger.LogWarning("Assignment history retrieval failed. LeadId={LeadId}", leadId);
        }

        return FromResult(result);
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead assignment started from assignment board.");

        var result = await handler.HandleAsync(new AssignFromBoardCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead assignment completed successfully from assignment board.");
        }
        else
        {
            _logger.LogWarning("Lead assignment failed from assignment board.");
        }

        return FromResult(result, "The lead was assigned.");
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead reassignment started from assignment board.");

        var result = await handler.HandleAsync(new ReassignFromBoardCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead reassignment completed successfully from assignment board.");
        }
        else
        {
            _logger.LogWarning("Lead reassignment failed from assignment board.");
        }

        return FromResult(result, "The lead was reassigned.");
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bulk lead routing started from assignment board.");

        var result = await handler.HandleAsync(new BulkRouteCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Bulk lead routing completed successfully from assignment board.");
        }
        else
        {
            _logger.LogWarning("Bulk lead routing failed from assignment board.");
        }

        return FromResult(result);
    }
}
