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
    TrackingAssetQueryHandler queries,
    ILogger<TrackingAssetsController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.TrackingAssetsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<TrackingAssetListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] TrackingAssetSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching tracking assets.");

        var result = await queries.HandleAsync(new SearchTrackingAssetsQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Tracking asset search failed.");
        }

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetTrackingAssetAsync))]
    [HasPermission(PermissionCodes.TrackingAssetsView)]
    [ProducesResponseType(typeof(ApiResponse<TrackingAssetDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrackingAssetAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting tracking asset {TrackingAssetId}.", id);

        var result = await queries.HandleAsync(new GetTrackingAssetQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get tracking asset {TrackingAssetId}.", id);
        }

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.TrackingAssetsExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] TrackingAssetSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting tracking assets.");

        var result = await queries.HandleAsync(new ExportTrackingAssetsQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Tracking asset export failed.");
        }
        else
        {
            logger.LogInformation("Tracking asset export completed successfully.");
        }

        return FileFromResult(result);
    }

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
        logger.LogInformation("Creating a new tracking asset.");

        var result = await commands.HandleAsync(
            new CreateTrackingAssetCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Tracking asset creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Tracking asset {TrackingAssetId} created successfully.", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetTrackingAssetAsync), new { id = result.Value!.Id },
            "Tracking asset created.");
    }

    /// <summary>Edits a Draft asset. The campaign cannot be changed - that would re-attribute gifts.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.TrackingAssetsEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateTrackingAssetRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new UpdateTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to update tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Tracking asset {TrackingAssetId} updated successfully.", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.TrackingAssetsSubmit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Submitting tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new SubmitTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to submit tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Tracking asset {TrackingAssetId} submitted successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>Refused for the person who created or submitted the asset.</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.TrackingAssetsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Approving tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new ApproveTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to approve tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Tracking asset {TrackingAssetId} approved successfully.", id);
        }

        return FromResult(result);
    }

    /// <summary>Approved to Active. Mints the tracking reference and the generated URL.</summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.TrackingAssetsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new ActivateTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to activate tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Tracking asset {TrackingAssetId} activated successfully.", id);
        }

        return FromResult(result);
    }

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
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Requesting disable for tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new RequestDisableTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to request disable for tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Disable requested successfully for tracking asset {TrackingAssetId}.", id);
        }

        return FromResult(result);
    }

    /// <summary>Decides a disable request, or takes a live asset down directly.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.TrackingAssetsDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deactivating tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new DeactivateTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to deactivate tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Tracking asset {TrackingAssetId} deactivated successfully.", id);
        }

        return FromResult(result);
    }

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
        Guid id, [FromBody] TrackingAssetLifecycleRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting draft tracking asset {TrackingAssetId}.", id);

        var result = await commands.HandleAsync(
            new DeleteDraftTrackingAssetCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to delete draft tracking asset {TrackingAssetId}.", id);
        }
        else
        {
            logger.LogInformation("Draft tracking asset {TrackingAssetId} deleted successfully.", id);
        }

        return FromResult(result);
    }
}