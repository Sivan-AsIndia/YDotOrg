using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.LeadWorkQueue.Commands.LeadWorkQueueActions;
using YDots.DON.Application.Features.LeadWorkQueue.DTOs;
using YDots.DON.Application.Features.LeadWorkQueue.Queries.GetLeadWorkQueue;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-001 Lead work queue. Prioritise new, due, overdue and nurture leads.
/// Route from the developer contract: /api/v1/donors/lead-work-queue.
/// </summary>
[Route("api/v1/donors/lead-work-queue")]
[Authorize]
public sealed class LeadWorkQueueController : ApiControllerBase
{
    /// <summary>GET the queue rows plus every filter option and the totals qualified by scope.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.LeadWorkQueueView)]
    [ProducesResponseType(typeof(ApiResponse<LeadWorkQueueResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] LeadSearchFilter filter,
        [FromServices] LeadWorkQueueQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetLeadWorkQueueQuery(filter), cancellationToken));

    /// <summary>GET one lead for the detail panel beside the queue.</summary>
    [HttpGet("{id:guid}", Name = "GetLeadFromWorkQueue")]
    [HasPermission(PermissionCodes.LeadWorkQueueView)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLead(
        Guid id,
        [FromServices] LeadWorkQueueQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetLeadDetailQuery(id), cancellationToken));

    /// <summary>POST accept. The caller takes ownership of the lead.</summary>
    [HttpPost("{id:guid}/accept")]
    [HasPermission(PermissionCodes.LeadWorkQueueAccept)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(
        Guid id,
        [FromBody] AcceptLeadRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new AcceptLeadCommand(id, request), cancellationToken),
            "The lead was accepted and assigned to you.");

    /// <summary>POST assign. Hands the lead to somebody else with a recorded reason.</summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(PermissionCodes.LeadWorkQueueAssign)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignLeadRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new AssignLeadCommand(id, request), cancellationToken),
            "The lead was assigned.");

    /// <summary>POST contact. Records the conversation and its outcome. Refused channels are blocked.</summary>
    [HttpPost("{id:guid}/contact")]
    [HasPermission(PermissionCodes.LeadWorkQueueContact)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Contact(
        Guid id,
        [FromBody] ContactLeadRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new ContactLeadCommand(id, request), cancellationToken),
            "The contact attempt was recorded.");

    /// <summary>POST qualify. Moves the lead to Qualified, or parks it in Nurture.</summary>
    [HttpPost("{id:guid}/qualify")]
    [HasPermission(PermissionCodes.LeadWorkQueueQualify)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Qualify(
        Guid id,
        [FromBody] QualifyLeadRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new QualifyLeadCommand(id, request), cancellationToken),
            "The lead was qualified.");

    /// <summary>POST close. Danger action: named reason, history preserved.</summary>
    [HttpPost("{id:guid}/close")]
    [HasPermission(PermissionCodes.LeadWorkQueueClose)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new CloseLeadCommand(id, request), cancellationToken),
            "The lead was closed.");

    /// <summary>
    /// POST convert. Step 5 of the guided flow: create or link the donor profile and preserve
    /// the lead history and its campaign attribution.
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    [HasPermission(PermissionCodes.DonorsCreate)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Convert(
        Guid id,
        [FromBody] ConvertLeadRequest request,
        [FromServices] LeadWorkQueueCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new ConvertLeadCommand(id, request), cancellationToken),
            "The lead was converted to a donor record.");
}
