using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Attribution.Commands.ManageAttribution;
using YDots.CAM.Application.Features.Attribution.DTOs;
using YDots.CAM.Application.Features.Attribution.Queries.AttributionQueries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// The attribution explorer: which campaign each donation was credited to, and why.
///
/// EVERY ROUTE HERE IS A READ EXCEPT ONE, and that one records a REQUEST rather than making a
/// change. Re-attributing a gift restates a campaign's income in every report that follows it, so
/// the correction itself is made where the donation lives - CAM records that somebody with grounds
/// has raised it.
///
/// UNATTRIBUTED DONATIONS ARE RETURNED, not filtered out. Many people type the address in rather
/// than following a link, and an explorer that showed only traced gifts would make the tracked
/// channels look like the whole picture - which is the number somebody would then use to decide
/// where next year's budget goes.
/// </summary>
[Route("api/v1/attribution")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class AttributionController(
    AttributionCommandHandler commands,
    AttributionQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.AttributionView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AttributionListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] AttributionSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchAttributionQuery(filter), cancellationToken));

    /// <summary>
    /// One donation's full attribution trail.
    ///
    /// THE HOPS ARE RETURNED IN ORDER - the link the donor followed, the asset it belonged to, the
    /// campaign that asset was created for - because "why is this gift credited to that campaign?"
    /// is usually answered by one of the hops rather than by the destination.
    /// </summary>
    [HttpGet("{donationId:guid}")]
    [HasPermission(PermissionCodes.AttributionView)]
    [ProducesResponseType(typeof(ApiResponse<AttributionDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid donationId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAttributionQuery(donationId), cancellationToken));

    /// <summary>
    /// How income breaks down by channel, source, medium and asset.
    ///
    /// EVERY SHARE IS OF THE TOTAL INCLUDING UNTRACED GIFTS. A channel reported as "60% of income"
    /// when it is 60% of the third that could be traced would overstate it threefold.
    /// </summary>
    [HttpGet("summary")]
    [HasPermission(PermissionCodes.AttributionView)]
    [ProducesResponseType(typeof(ApiResponse<AttributionSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummaryAsync(
        [FromQuery] Guid? campaignId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetAttributionSummaryQuery(campaignId), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.AttributionExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] AttributionSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportAttributionQuery(filter), cancellationToken));

    /// <summary>
    /// Asks for a donation's attribution to be looked at again.
    ///
    /// IT DOES NOT CHANGE THE DONATION, and the response says so in as many words. At most one open
    /// request per donation, so two people cannot end up investigating the same gift without
    /// knowing about each other.
    /// </summary>
    [HttpPost("correction-requests")]
    [HasPermission(PermissionCodes.AttributionRequestCorrection)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestCorrectionAsync(
        [FromBody] RequestAttributionCorrectionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new RequestAttributionCorrectionCommand(request), cancellationToken));

    /// <summary>
    /// Closes a correction request.
    ///
    /// "CHECKED AND CORRECT" IS RECORDED SEPARATELY FROM AN ACTUAL CHANGE, which is what lets
    /// somebody tell how often tracking is really getting it wrong before spending on more of it.
    /// </summary>
    [HttpPost("correction-requests/{id:guid}/resolve")]
    [HasPermission(PermissionCodes.AttributionRequestCorrection)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveCorrectionAsync(
        Guid id,
        [FromBody] ResolveAttributionCorrectionBody body,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ResolveAttributionCorrectionCommand(
                id, body.ResolutionNote, body.AttributionChanged, body.ExpectedVersion),
            cancellationToken));
}

/// <summary>The body of a resolve call.</summary>
public sealed record ResolveAttributionCorrectionBody
{
    public long ExpectedVersion { get; init; }

    /// <summary>What was decided. Required, so the person who raised it learns what happened.</summary>
    public string ResolutionNote { get; init; } = string.Empty;

    /// <summary>Whether the attribution was actually changed as a result.</summary>
    public bool AttributionChanged { get; init; }
}
