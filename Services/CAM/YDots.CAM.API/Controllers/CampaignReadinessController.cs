using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.CampaignReadiness.Commands.ManageReadiness;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;
using YDots.CAM.Application.Features.CampaignReadiness.Queries.ReadinessQueries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// The campaign readiness checklist.
///
/// THE CHECKLIST IS A GATE. A campaign cannot go Active while a required check has not passed,
/// and that rule is enforced by the ACTIVATE endpoint on the campaigns controller rather than
/// here - one gate, in the place the transition happens.
///
/// WHAT MOVED OUT OF THIS CONTROLLER. It used to carry <c>readiness/request-approval</c> and
/// <c>readiness/approve</c>, which moved a campaign through Submitted to Approved from inside
/// the readiness feature. That was a second approval path with its own copy of the status rules
/// and NO segregation-of-duties check - so a Campaign Manager who could not approve a campaign
/// on the campaigns endpoint could approve the same campaign here. Campaign approval now
/// happens in exactly one place.
/// </summary>
[Route("api/v1")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class CampaignReadinessController(
    ReadinessCommandHandler commands,
    ReadinessQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// The whole checklist for one campaign, with its launch verdict.
    ///
    /// NOT PAGED, deliberately: the question it answers is "can this campaign launch?", and
    /// half a checklist cannot answer it.
    /// </summary>
    [HttpGet("campaigns/{campaignId:guid}/readiness")]
    [HasPermission(PermissionCodes.ReadinessView)]
    [ProducesResponseType(typeof(ApiResponse<CampaignReadinessResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReadinessAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetCampaignReadinessQuery(campaignId), cancellationToken));

    [HttpGet("readiness-checks/{id:guid}", Name = nameof(GetCheckAsync))]
    [HasPermission(PermissionCodes.ReadinessView)]
    [ProducesResponseType(typeof(ApiResponse<ReadinessCheckDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCheckAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetReadinessCheckQuery(id), cancellationToken));

    [HttpPost("campaigns/{campaignId:guid}/readiness-checks")]
    [HasPermission(PermissionCodes.ReadinessCreate)]
    [ProducesResponseType(typeof(ApiResponse<ReadinessCheckDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCheckAsync(
        Guid campaignId,
        [FromBody] CreateReadinessCheckRequest request,
        CancellationToken cancellationToken)
    {
        var result = await commands.HandleAsync(
            new CreateReadinessCheckCommand(campaignId, request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetCheckAsync), new { id = result.Value!.Id }, "Readiness check added.");
    }

    /// <summary>Edits a check. Only a Pending check may be edited.</summary>
    [HttpPut("readiness-checks/{id:guid}")]
    [HasPermission(PermissionCodes.ReadinessEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCheckAsync(
        Guid id, [FromBody] UpdateReadinessCheckRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new UpdateReadinessCheckCommand(id, request), cancellationToken));

    /// <summary>
    /// Signs a check off as passed.
    ///
    /// SEPARATELY PERMISSIONED FROM FAILING, so an Organisation can let somebody record a
    /// failure without letting them sign a check off. Refused while a blocker is open.
    /// </summary>
    [HttpPost("readiness-checks/{id:guid}/pass")]
    [HasPermission(PermissionCodes.ReadinessPass)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PassCheckAsync(
        Guid id, [FromBody] ReadinessVerdictRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new PassReadinessCheckCommand(id, request), cancellationToken));

    [HttpPost("readiness-checks/{id:guid}/fail")]
    [HasPermission(PermissionCodes.ReadinessFail)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FailCheckAsync(
        Guid id, [FromBody] ReadinessVerdictRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new FailReadinessCheckCommand(id, request), cancellationToken));

    /// <summary>Raises a blocker. This also fails the check it is raised against.</summary>
    [HttpPost("readiness-checks/{id:guid}/blockers")]
    [HasPermission(PermissionCodes.ReadinessManageBlockers)]
    [ProducesResponseType(typeof(ApiResponse<ReadinessBlockerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddBlockerAsync(
        Guid id, [FromBody] AssignReadinessBlockerRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new AssignReadinessBlockerCommand(id, request), cancellationToken));

    /// <summary>
    /// Clears a blocker.
    ///
    /// The check goes back to PENDING, not to Passed: clearing the obstacle is not the same as
    /// verifying the thing, and auto-passing would make raise-then-clear a way of skipping the
    /// verification entirely.
    /// </summary>
    [HttpPost("readiness-blockers/{blockerId:guid}/resolve")]
    [HasPermission(PermissionCodes.ReadinessManageBlockers)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveBlockerAsync(
        Guid blockerId,
        [FromBody] ResolveReadinessBlockerRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ResolveReadinessBlockerCommand(blockerId, request), cancellationToken));

    /// <summary>Sends a campaign back to Draft. A reason is mandatory.</summary>
    [HttpPost("campaigns/{campaignId:guid}/readiness/return-to-draft")]
    [HasPermission(PermissionCodes.ReadinessReturnToDraft)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReturnToDraftAsync(
        Guid campaignId,
        [FromBody] ReturnCampaignToDraftRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ReturnCampaignToDraftCommand(campaignId, request), cancellationToken));
}
