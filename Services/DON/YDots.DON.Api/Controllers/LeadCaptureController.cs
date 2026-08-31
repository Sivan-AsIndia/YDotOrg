using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.LeadCapture.Commands.CaptureLead;
using YDots.DON.Application.Features.LeadCapture.DTOs;
using YDots.DON.Application.Features.LeadCapture.Queries.GetLeadCapture;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-002 Lead capture. Create a minimum-data lead with source evidence and consent context.
/// Route from the developer contract: /api/v1/donors/lead-capture.
/// </summary>
[Route("api/v1/donors/lead-capture")]
[Authorize]
public sealed class LeadCaptureController : ApiControllerBase
{
    /// <summary>GET a blank capture form, or an existing draft when leadId is supplied.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.LeadCaptureView)]
    [ProducesResponseType(typeof(ApiResponse<LeadCaptureResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForm(
        [FromQuery] Guid? leadId,
        [FromServices] GetLeadCaptureQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetLeadCaptureQuery(leadId), cancellationToken));

    /// <summary>GET one saved lead by id.</summary>
    [HttpGet("{id:guid}", Name = "GetCapturedLead")]
    [HasPermission(PermissionCodes.LeadCaptureView)]
    [ProducesResponseType(typeof(ApiResponse<LeadCaptureResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] GetLeadCaptureQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetLeadCaptureQuery(id), cancellationToken));

    /// <summary>POST save. Creates the draft lead and, when the consent toggle is on, its consent rows.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.LeadCaptureSave)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(
        [FromBody] CreateLeadRequest request,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new SaveLeadCommand(request), cancellationToken);

        return CreatedFromResult(result, "GetCapturedLead", new { id = result.Value?.Id ?? Guid.Empty },
            "The lead was saved.");
    }

    /// <summary>PUT save on an existing draft.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.LeadCaptureSave)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateLeadRequest request,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new UpdateLeadCommand(id, request), cancellationToken),
            "The lead was saved.");

    /// <summary>
    /// POST deduplicate. Read-only: it reports safe candidate categories and comparison routes,
    /// and never exposes another person's protected details.
    /// </summary>
    [HttpPost("{id:guid}/deduplicate")]
    [HasPermission(PermissionCodes.LeadCaptureDeduplicate)]
    [ProducesResponseType(typeof(ApiResponse<DeduplicateResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deduplicate(
        Guid id,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new DeduplicateLeadCommand(id), cancellationToken));

    /// <summary>
    /// POST submit. Promotes the draft into the work queue. Send an Idempotency-Key header and a
    /// retry after an uncertain response returns the same record instead of creating a second one.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.LeadCaptureSubmit)]
    [ProducesResponseType(typeof(ApiResponse<LeadDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(
        Guid id,
        [FromBody] TransitionRequest request,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new SubmitLeadCommand(id, request), cancellationToken),
            "The lead was submitted to the work queue.");

    /// <summary>DELETE an unused draft. Only for a draft with no consent, assignment or donor reference.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.LeadCaptureDeleteDraft)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDraft(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new DeleteLeadDraftCommand(id, request), cancellationToken));
}
