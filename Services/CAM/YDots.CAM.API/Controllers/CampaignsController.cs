using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Campaigns.Commands.CampaignLifecycle;
using YDots.CAM.Application.Features.Campaigns.Commands.ManageCampaign;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Application.Features.Campaigns.Queries.CampaignQueries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// Campaigns: the register, the wizard, the detail screen and every lifecycle transition.
///
/// EVERY ROUTE REQUIRES A RESOLVED ORGANISATION. A campaign belongs to exactly one, so there is
/// no meaningful platform-wide view of them - unlike the IAM master catalogue, where SuperAdmin
/// genuinely maintains shared rows.
///
/// EACH LIFECYCLE TRANSITION IS ITS OWN ROUTE WITH ITS OWN PERMISSION, rather than one
/// "change status" endpoint taking the target status. That is what lets an Organisation grant
/// somebody the ability to pause a campaign without also granting the ability to approve one -
/// a distinction a single endpoint could not express.
/// </summary>
[Route("api/v1/campaigns")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class CampaignsController(
    CampaignCommandHandler commands,
    CampaignLifecycleCommandHandler lifecycle,
    CampaignQueryHandler queries,
    ILogger<CampaignsController> logger) : ApiControllerBase
{
    // =====================================================================================
    // Reading
    // =====================================================================================

    /// <summary>The campaign register.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.CampaignsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<CampaignListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] CampaignSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogDebug("Searching campaigns.");

        var result = await queries.HandleAsync(new SearchCampaignsQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Campaign search failed.");
        }

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetCampaignAsync))]
    [HasPermission(PermissionCodes.CampaignsView)]
    [ProducesResponseType(typeof(ApiResponse<CampaignDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaignAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting campaign {CampaignId}.", id);

        var result = await queries.HandleAsync(new GetCampaignQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get campaign {CampaignId}.", id);
        }

        return FromResult(result);
    }

    /// <summary>Counts by status, for the register's summary tiles.</summary>
    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.CampaignsView)]
    [ProducesResponseType(typeof(ApiResponse<CampaignStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting campaign statistics.");

        var result = await queries.HandleAsync(new GetCampaignStatisticsQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get campaign statistics.");
        }

        return FromResult(result);
    }

    /// <summary>Selectable campaigns for a picker. Closed and cancelled ones are excluded.</summary>
    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.CampaignsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LookupItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(
        [FromQuery] string? search, [FromQuery] int take, CancellationToken cancellationToken)
    {
        logger.LogDebug("Looking up campaigns.");

        var result = await queries.HandleAsync(
            new LookupCampaignsQuery(search, take <= 0 ? 50 : take), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Campaign lookup failed.");
        }

        return FromResult(result);
    }

    /// <summary>The audit trail for one campaign, newest first.</summary>
    [HttpGet("{id:guid}/history")]
    [HasPermission(PermissionCodes.CampaignsViewHistory)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<CampaignHistoryResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistoryAsync(
        Guid id, [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting campaign history for {CampaignId}.", id);

        var result = await queries.HandleAsync(
            new GetCampaignHistoryQuery(id, pagination), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get campaign history for {CampaignId}.", id);
        }

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.CampaignsExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] CampaignSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting campaigns.");

        var result = await queries.HandleAsync(new ExportCampaignsQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Campaign export failed.");
        }
        else
        {
            logger.LogInformation("Campaign export completed successfully.");
        }

        return FileFromResult(result);
    }

    // =====================================================================================
    // Writing
    // =====================================================================================

    [HttpPost]
    [HasPermission(PermissionCodes.CampaignsCreate)]
    [ProducesResponseType(typeof(ApiResponse<CampaignDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating a new campaign.");

        var result = await commands.HandleAsync(new CreateCampaignCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Campaign creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Campaign {CampaignId} created successfully.", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetCampaignAsync), new { id = result.Value!.Id }, "Campaign created.");
    }

    /// <summary>Edits a Draft campaign. Refused once it has been submitted.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.CampaignsEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateCampaignRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating campaign {CampaignId}.", id);

        var result = await commands.HandleAsync(
            new UpdateCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to update campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} updated successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>Deletes a Draft campaign. Refused while any tracking asset hangs off it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.CampaignsDeleteDraft)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDraftAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting draft campaign {CampaignId}.", id);

        var result = await commands.HandleAsync(
            new DeleteDraftCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to delete draft campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Draft campaign {CampaignId} deleted successfully.", id);
        }

        return FromResult(result);
    }

    // =====================================================================================
    // Lifecycle
    // =====================================================================================

    /// <summary>
    /// Draft to Submitted.
    ///
    /// EVERY submission lands on Submitted and waits for a second person, a platform
    /// administrator's included. A super admin's submission used to be approved in the same step;
    /// that made one account both submitter and approver of the same campaign, and it has been
    /// removed along with the setting that controlled it. Approval is a separate call, made by
    /// somebody who did not raise the record.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.CampaignsSubmit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Submitting campaign {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new SubmitCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to submit campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} submitted successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Submitted to Approved.
    ///
    /// REFUSED FOR THE PERSON WHO CREATED OR SUBMITTED IT, with a distinct
    /// SEGREGATION_OF_DUTIES_VIOLATION error code so the screen can explain why rather than
    /// showing a bare "forbidden".
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.CampaignsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Approving campaign {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new ApproveCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to approve campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} approved successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Approved or Scheduled to Active.
    ///
    /// Refused while a required readiness check has not passed. The refusal NAMES the
    /// outstanding checks as field errors, so the operator is told what to go and fix.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.CampaignsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating campaign {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new ActivateCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to activate campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} activated successfully.", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/pause")]
    [HasPermission(PermissionCodes.CampaignsPause)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PauseAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Pausing campaign {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new PauseCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to pause campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} paused successfully.", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/resume")]
    [HasPermission(PermissionCodes.CampaignsResume)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResumeAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resuming campaign {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new ResumeCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to resume campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign {CampaignId} resumed successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Raises a close request. The campaign moves to Closing and waits for a second person.
    ///
    /// A reason category and a detailed reason are BOTH mandatory here, unlike the other
    /// transitions: closing a campaign is the one somebody will be asked to justify later.
    /// </summary>
    [HttpPost("{id:guid}/request-close")]
    [HasPermission(PermissionCodes.CampaignsRequestClose)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestCloseAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Requesting campaign closure for {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new RequestCloseCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to request closure for campaign {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign closure requested successfully for {CampaignId}.", id);
        }

        return FromResult(result);
    }

    /// <summary>Approves an outstanding close request. Refused for the person who raised it.</summary>
    [HttpPost("{id:guid}/approve-close")]
    [HasPermission(PermissionCodes.CampaignsApproveClose)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveCloseAsync(
        Guid id, [FromBody] CampaignLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Approving campaign closure for {CampaignId}.", id);

        var result = await lifecycle.HandleAsync(
            new ApproveCloseCampaignCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to approve campaign closure for {CampaignId}.", id);
        }
        else
        {
            logger.LogInformation("Campaign closure approved successfully for {CampaignId}.", id);
        }

        return FromResult(result);
    }
}