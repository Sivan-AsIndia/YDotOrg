using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.Commands.PlanFollowUp;
using YDots.DON.Application.Features.FollowUpPlanner.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.Queries.GetFollowUpPlanner;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// DON-UI-08 Follow-up planner. Plan a respectful, consent-aware next action with clear
/// ownership and due time.
/// </summary>
[Route("api/v1/donors/follow-up-planner")]
[Authorize]
public sealed class FollowUpPlannerController : ApiControllerBase
{
    private readonly ILogger<FollowUpPlannerController> _logger;

    public FollowUpPlannerController(ILogger<FollowUpPlannerController> logger)
    {
        _logger = logger;
    }

    /// <summary>GET the planned follow-ups plus every catalogue the form needs.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.FollowUpPlannerView)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpPlannerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlanner(
        [FromQuery] FollowUpSearchFilter filter,
        [FromServices] FollowUpPlannerQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up planner retrieval started.");

        var result = await handler.HandleAsync(new GetFollowUpPlannerQuery(filter), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up planner retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Follow-up planner retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>
    /// GET the consent warning for a donor or lead. The screen calls this as soon as a record is
    /// chosen, so the warning appears before the rest of the form is filled in.
    /// </summary>
    [HttpGet("consent-warning")]
    [HasPermission(PermissionCodes.FollowUpPlannerView)]
    [ProducesResponseType(typeof(ApiResponse<ConsentWarningResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetConsentWarning(
        [FromQuery] Guid? donorId,
        [FromQuery] Guid? leadId,
        [FromServices] FollowUpPlannerQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up consent warning retrieval started.");

        var result = await handler.HandleAsync(new GetConsentWarningQuery(donorId, leadId), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up consent warning retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Follow-up consent warning retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>GET one follow-up.</summary>
    [HttpGet("{id:guid}", Name = "GetFollowUpById")]
    [HasPermission(PermissionCodes.FollowUpPlannerView)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] FollowUpPlannerQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up detail retrieval started. FollowUpId={FollowUpId}", id);

        var result = await handler.HandleAsync(new GetFollowUpDetailQuery(id), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up detail retrieval completed successfully. FollowUpId={FollowUpId}", id);
        }
        else
        {
            _logger.LogWarning("Follow-up detail retrieval failed. FollowUpId={FollowUpId}", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// POST schedule follow-up. The primary action. Refused by the server when the chosen
    /// channel is not permitted by consent, rather than merely warned about.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.FollowUpPlannerSchedule)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Schedule(
        [FromBody] ScheduleFollowUpRequest request,
        [FromServices] FollowUpCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up scheduling started.");

        var result = await handler.HandleAsync(new ScheduleFollowUpCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up scheduling completed successfully. FollowUpId={FollowUpId}", result.Value?.Id);
        }
        else
        {
            _logger.LogWarning("Follow-up scheduling failed.");
        }

        return CreatedFromResult(result, "GetFollowUpById", new { id = result.Value?.Id ?? Guid.Empty },
            "The follow-up was scheduled.");
    }

    /// <summary>POST assign. Hands the task to a different owner.</summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(PermissionCodes.FollowUpPlannerAssign)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignFollowUpRequest request,
        [FromServices] FollowUpCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up assignment started. FollowUpId={FollowUpId}", id);

        var result = await handler.HandleAsync(new AssignFollowUpCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up assignment completed successfully. FollowUpId={FollowUpId}", id);
        }
        else
        {
            _logger.LogWarning("Follow-up assignment failed. FollowUpId={FollowUpId}", id);
        }

        return FromResult(result, "The follow-up was assigned.");
    }

    /// <summary>POST mark complete. Also writes the conversation into the donor interaction log.</summary>
    [HttpPost("{id:guid}/mark-complete")]
    [HasPermission(PermissionCodes.FollowUpPlannerMarkComplete)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkComplete(
        Guid id,
        [FromBody] CompleteFollowUpRequest request,
        [FromServices] FollowUpCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up completion started. FollowUpId={FollowUpId}", id);

        var result = await handler.HandleAsync(new CompleteFollowUpCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up completion completed successfully. FollowUpId={FollowUpId}", id);
        }
        else
        {
            _logger.LogWarning("Follow-up completion failed. FollowUpId={FollowUpId}", id);
        }

        return FromResult(result, "The follow-up was completed.");
    }

    /// <summary>POST reschedule.</summary>
    [HttpPost("{id:guid}/reschedule")]
    [HasPermission(PermissionCodes.FollowUpPlannerReschedule)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        Guid id,
        [FromBody] RescheduleFollowUpRequest request,
        [FromServices] FollowUpCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up rescheduling started. FollowUpId={FollowUpId}", id);

        var result = await handler.HandleAsync(new RescheduleFollowUpCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up rescheduling completed successfully. FollowUpId={FollowUpId}", id);
        }
        else
        {
            _logger.LogWarning("Follow-up rescheduling failed. FollowUpId={FollowUpId}", id);
        }

        return FromResult(result, "The follow-up was rescheduled.");
    }

    /// <summary>POST cancel task. Danger action: named reason required.</summary>
    [HttpPost("{id:guid}/cancel-task")]
    [HasPermission(PermissionCodes.FollowUpPlannerCancelTask)]
    [ProducesResponseType(typeof(ApiResponse<FollowUpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelTask(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] FollowUpCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Follow-up cancellation started. FollowUpId={FollowUpId}", id);

        var result = await handler.HandleAsync(new CancelFollowUpCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Follow-up cancellation completed successfully. FollowUpId={FollowUpId}", id);
        }
        else
        {
            _logger.LogWarning("Follow-up cancellation failed. FollowUpId={FollowUpId}", id);
        }

        return FromResult(result, "The follow-up was cancelled.");
    }
}