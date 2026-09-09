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
    private readonly ILogger<LeadCaptureController> _logger;

    public LeadCaptureController(ILogger<LeadCaptureController> logger)
    {
        _logger = logger;
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead capture form retrieval started.");

        var result = await handler.HandleAsync(new GetLeadCaptureQuery(leadId), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead capture form retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Lead capture form retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>GET one saved lead by id.</summary>
    [HttpGet("{id:guid}", Name = "GetCapturedLead")]
    [HasPermission(PermissionCodes.LeadCaptureView)]
    [ProducesResponseType(typeof(ApiResponse<LeadCaptureResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] GetLeadCaptureQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Captured lead retrieval started. LeadId={LeadId}", id);

        var result = await handler.HandleAsync(new GetLeadCaptureQuery(id), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Captured lead retrieval completed successfully. LeadId={LeadId}", id);
        }
        else
        {
            _logger.LogWarning("Captured lead retrieval failed. LeadId={LeadId}", id);
        }

        return FromResult(result);
    }

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
        _logger.LogInformation("Lead draft creation started.");

        var result = await handler.HandleAsync(new SaveLeadCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead draft creation completed successfully. LeadId={LeadId}", result.Value?.Id);
        }
        else
        {
            _logger.LogWarning("Lead draft creation failed.");
        }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead draft update started. LeadId={LeadId}", id);

        var result = await handler.HandleAsync(new UpdateLeadCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead draft update completed successfully. LeadId={LeadId}", id);
        }
        else
        {
            _logger.LogWarning("Lead draft update failed. LeadId={LeadId}", id);
        }

        return FromResult(result, "The lead was saved.");
    }

    /// <summary>
    /// POST deduplicate. Read-only: it reports safe candidate categories and comparison routes,
    /// and never exposes another person's protected details.
    /// </summary>
    /// <summary>
    /// POST bulk-import. Creates many leads from the rows of an uploaded file.
    ///
    /// SUBMIT IS THE PERMISSION, NOT SAVE, AND THE DIFFERENCE MATTERS. Save is classified as an
    /// Edit, which an APPROVER holds - the capture screen relies on that being harmless because a
    /// saved lead is a DRAFT, and Submit is the gate that puts it in the work queue. A bulk upload
    /// has no draft stage: its rows go straight into the queue. Gating it on Save would therefore
    /// let an APPROVER create two hundred live leads while still being unable to submit a single
    /// one by hand, which is precisely the maker-checker split the role model exists to keep.
    /// </summary>
    [HttpPost("bulk-import")]
    [HasPermission(PermissionCodes.LeadCaptureSubmit)]
    [ProducesResponseType(typeof(ApiResponse<BulkLeadImportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BulkImport(
        [FromBody] BulkLeadImportRequest request,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bulk lead import started.");

        var result = await handler.HandleAsync(new BulkImportLeadsCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Bulk lead import completed successfully.");
        }
        else
        {
            _logger.LogWarning("Bulk lead import failed.");
        }

        return FromResult(result, "The upload was processed.");
    }

    [HttpPost("{id:guid}/deduplicate")]
    [HasPermission(PermissionCodes.LeadCaptureDeduplicate)]
    [ProducesResponseType(typeof(ApiResponse<DeduplicateResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deduplicate(
        Guid id,
        [FromServices] LeadCaptureCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead deduplication started. LeadId={LeadId}", id);

        var result = await handler.HandleAsync(new DeduplicateLeadCommand(id), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead deduplication completed successfully. LeadId={LeadId}", id);
        }
        else
        {
            _logger.LogWarning("Lead deduplication failed. LeadId={LeadId}", id);
        }

        return FromResult(result);
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead submission started. LeadId={LeadId}", id);

        var result = await handler.HandleAsync(new SubmitLeadCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead submission completed successfully. LeadId={LeadId}", id);
        }
        else
        {
            _logger.LogWarning("Lead submission failed. LeadId={LeadId}", id);
        }

        return FromResult(result, "The lead was submitted to the work queue.");
    }

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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Lead draft deletion started. LeadId={LeadId}", id);

        var result = await handler.HandleAsync(new DeleteLeadDraftCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Lead draft deletion completed successfully. LeadId={LeadId}", id);
        }
        else
        {
            _logger.LogWarning("Lead draft deletion failed. LeadId={LeadId}", id);
        }

        return FromResult(result);
    }
}