using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.BudgetTargetPlans.Commands.ManageBudgetPlan;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;
using YDots.CAM.Application.Features.BudgetTargetPlans.Queries.BudgetPlanQueries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// Budget and target plans.
///
/// A PLAN IS AN IDENTITY WITH A HISTORY, and the routes say so: the plan is addressed by its own
/// id, while every action that changes figures addresses a VERSION. That is what stops an
/// "update the plan" call quietly rewriting figures somebody has already approved.
///
/// SUBMIT AND APPROVE ARE SEPARATE ENDPOINTS with separate permissions, and the handler refuses to
/// let one person do both to the same version. Two endpoints is what makes that rule expressible:
/// an organisation can grant somebody the right to prepare a budget without the right to commit
/// money to it.
/// </summary>
[Route("api/v1")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class BudgetTargetPlansController(
    BudgetPlanCommandHandler commands,
    BudgetPlanQueryHandler queries,
    ILogger<BudgetTargetPlansController> logger) : ApiControllerBase
{
    // =============================================================================================
    // Reading
    // =============================================================================================

    [HttpGet("budget-plans")]
    [HasPermission(PermissionCodes.BudgetPlansView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<BudgetPlanListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] BudgetPlanSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogDebug("Searching budget plans.");

        var result = await queries.HandleAsync(new SearchBudgetPlansQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Budget plan search failed.");
        }

        return FromResult(result);
    }

    [HttpGet("budget-plans/{id:guid}", Name = nameof(GetPlanAsync))]
    [HasPermission(PermissionCodes.BudgetPlansView)]
    [ProducesResponseType(typeof(ApiResponse<BudgetPlanDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting budget plan {BudgetPlanId}.", id);

        var result = await queries.HandleAsync(new GetBudgetPlanQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get budget plan {BudgetPlanId}.", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// A campaign's committed budget.
    ///
    /// APPROVED VERSIONS ONLY, one per plan. It is the figure a campaign detail page shows next to
    /// what has actually come in, and it must never include figures nobody has agreed to.
    /// </summary>
    [HttpGet("campaigns/{campaignId:guid}/budget-summary")]
    [HasPermission(PermissionCodes.BudgetPlansView)]
    [ProducesResponseType(typeof(ApiResponse<CampaignBudgetSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaignSummaryAsync(
        Guid campaignId, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting budget summary for campaign {CampaignId}.", campaignId);

        var result = await queries.HandleAsync(
            new GetCampaignBudgetSummaryQuery(campaignId), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get budget summary for campaign {CampaignId}.", campaignId);
        }

        return FromResult(result);
    }

    [HttpGet("budget-plans/export")]
    [HasPermission(PermissionCodes.BudgetPlansExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] BudgetPlanSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting budget plans.");

        var result = await queries.HandleAsync(new ExportBudgetPlansQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Budget plan export failed.");
        }
        else
        {
            logger.LogInformation("Budget plan export completed successfully.");
        }

        return FileFromResult(result);
    }

    // =============================================================================================
    // Writing
    // =============================================================================================

    /// <summary>
    /// Allocates a plan and its first draft version.
    ///
    /// THE REFERENCE COMES BACK FROM HERE. It is minted server-side, so a client must never compose
    /// one - two people allocating at the same moment would otherwise be free to mint the same
    /// code, and a plan reference is what a finance team quotes.
    /// </summary>
    [HttpPost("budget-plans")]
    [HasPermission(PermissionCodes.BudgetPlansAllocate)]
    [ProducesResponseType(typeof(ApiResponse<BudgetPlanDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AllocateAsync(
        [FromBody] AllocateBudgetPlanRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Allocating a new budget plan.");

        var result = await commands.HandleAsync(
            new AllocateBudgetPlanCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Budget plan allocation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Budget plan {BudgetPlanId} allocated successfully.", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetPlanAsync), new { id = result.Value!.Id }, "Budget plan allocated.");
    }

    /// <summary>
    /// Revises a plan into a NEW version.
    ///
    /// IT IS A POST, NOT A PUT, because it creates something. A PUT here would suggest the plan's
    /// figures were being replaced - which is exactly the operation this design exists to prevent.
    /// </summary>
    [HttpPost("budget-plans/{id:guid}/revisions")]
    [HasPermission(PermissionCodes.BudgetPlansRevise)]
    [ProducesResponseType(typeof(ApiResponse<BudgetPlanDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReviseAsync(
        Guid id, [FromBody] ReviseBudgetPlanRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating a new revision for budget plan {BudgetPlanId}.", id);

        var result = await commands.HandleAsync(
            new ReviseBudgetPlanCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to create a new revision for budget plan {BudgetPlanId}.", id);
        }
        else
        {
            logger.LogInformation("New revision created successfully for budget plan {BudgetPlanId}.", id);
        }

        return FromResult(result);
    }

    /// <summary>Edits a draft version in place. Refused on anything already submitted.</summary>
    [HttpPut("budget-plan-versions/{versionId:guid}")]
    [HasPermission(PermissionCodes.BudgetPlansRevise)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateVersionAsync(
        Guid versionId,
        [FromBody] UpdateBudgetPlanVersionRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating budget plan version {BudgetPlanVersionId}.", versionId);

        var result = await commands.HandleAsync(
            new UpdateBudgetPlanVersionCommand(versionId, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to update budget plan version {BudgetPlanVersionId}.", versionId);
        }
        else
        {
            logger.LogInformation("Budget plan version {BudgetPlanVersionId} updated successfully.", versionId);
        }

        return FromResult(result);
    }

    [HttpPost("budget-plan-versions/{versionId:guid}/submit")]
    [HasPermission(PermissionCodes.BudgetPlansSubmit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitVersionAsync(
        Guid versionId,
        [FromBody] SubmitBudgetPlanVersionRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Submitting budget plan version {BudgetPlanVersionId}.", versionId);

        var result = await commands.HandleAsync(
            new SubmitBudgetPlanVersionCommand(versionId, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to submit budget plan version {BudgetPlanVersionId}.", versionId);
        }
        else
        {
            logger.LogInformation("Budget plan version {BudgetPlanVersionId} submitted successfully.", versionId);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Approves a version, making its figures the plan's committed budget.
    ///
    /// THE SUBMITTER IS REFUSED, with a 403 that says so. A budget is where an organisation commits
    /// its money, and one person doing both ends is exactly the control an auditor looks for.
    /// </summary>
    [HttpPost("budget-plan-versions/{versionId:guid}/approve")]
    [HasPermission(PermissionCodes.BudgetPlansApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveVersionAsync(
        Guid versionId,
        [FromBody] BudgetPlanDecisionRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Approving budget plan version {BudgetPlanVersionId}.", versionId);

        var result = await commands.HandleAsync(
            new ApproveBudgetPlanVersionCommand(versionId, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to approve budget plan version {BudgetPlanVersionId}.", versionId);
        }
        else
        {
            logger.LogInformation("Budget plan version {BudgetPlanVersionId} approved successfully.", versionId);
        }

        return FromResult(result);
    }

    [HttpPost("budget-plan-versions/{versionId:guid}/reject")]
    [HasPermission(PermissionCodes.BudgetPlansReject)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectVersionAsync(
        Guid versionId,
        [FromBody] BudgetPlanDecisionRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Rejecting budget plan version {BudgetPlanVersionId}.", versionId);

        var result = await commands.HandleAsync(
            new RejectBudgetPlanVersionCommand(versionId, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to reject budget plan version {BudgetPlanVersionId}.", versionId);
        }
        else
        {
            logger.LogInformation("Budget plan version {BudgetPlanVersionId} rejected successfully.", versionId);
        }

        return FromResult(result);
    }
}