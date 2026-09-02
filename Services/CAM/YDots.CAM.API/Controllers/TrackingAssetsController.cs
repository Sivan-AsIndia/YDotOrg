using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.TrackingAssets.Commands.ManageTrackingAsset;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;
using YDots.CAM.Application.Features.TrackingAssets.Queries.TrackingAssetQueries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// Tracking assets: QR codes, short links, UTM links and landing pages.
///
/// THE TRACKING REFERENCE IS MINTED ON ACTIVATION and never regenerated. It is the attribution
/// key a donation carries back from the public flow, and a QR code carrying it may already be
/// printed - so an asset deactivated and reactivated keeps the reference it had.
/// </summary>
[Route("api/v1/tracking-assets")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class TrackingAssetsController(
    TrackingAssetCommandHandler commands,
    TrackingAssetQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.TrackingAssetsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<TrackingAssetListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] TrackingAssetSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchTrackingAssetsQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetTrackingAssetAsync))]
    [HasPermission(PermissionCodes.TrackingAssetsView)]
    [ProducesResponseType(typeof(ApiResponse<TrackingAssetDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrackingAssetAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetTrackingAssetQuery(id), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.TrackingAssetsExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] TrackingAssetSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportTrackingAssetsQuery(filter), cancellationToken));

    /// <summary>
    /// Creates a tracking asset.
    ///
    /// Placements are REQUIRED for an Offline channel and REFUSED for every other, because a
    /// placement describes where a physical asset was put.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.TrackingAssetsCreate)]
    [ProducesResponseType(typeof(ApiResponse<TrackingAssetDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateTrackingAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await commands.HandleAsync(new CreateTrackingAssetCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetTrackingAssetAsync), new { id = result.Value!.Id },
                "Tracking asset created.");
    }

    /// <summary>Edits a Draft asset. The campaign cannot be changed - that would re-attribute gifts.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.TrackingAssetsEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateTrackingAssetRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new UpdateTrackingAssetCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.TrackingAssetsSubmit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new SubmitTrackingAssetCommand(id, request), cancellationToken));

    /// <summary>Refused for the person who created or submitted the asset.</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.TrackingAssetsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ApproveTrackingAssetCommand(id, request), cancellationToken));

    /// <summary>Approved to Active. Mints the tracking reference and the generated URL.</summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.TrackingAssetsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ActivateTrackingAssetCommand(id, request), cancellationToken));

    /// <summary>
    /// Asks for a live asset to be taken down. Active to DisableRequested.
    ///
    /// THE MAKER'S HALF of the disable pair: taking an asset down stops a printed QR code
    /// resolving, so the person who made it asks and somebody else decides.
    /// </summary>
    [HttpPost("{id:guid}/request-disable")]
    [HasPermission(PermissionCodes.TrackingAssetsRequestDisable)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestDisableAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new RequestDisableTrackingAssetCommand(id, request), cancellationToken));

    /// <summary>Decides a disable request, or takes a live asset down directly.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.TrackingAssetsDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new DeactivateTrackingAssetCommand(id, request), cancellationToken));

    /// <summary>
    /// Destroys an unused Draft asset.
    ///
    /// THE ONLY DELETE IN THE MODULE. It is safe only because a Draft has never been activated:
    /// it holds no tracking reference, so no donation can have been attributed through it.
    /// Anything past Draft is retired by deactivating it instead.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.TrackingAssetsDeleteDraft)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDraftAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new DeleteDraftTrackingAssetCommand(id, request), cancellationToken));
}
