using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.ReferenceData.DTOs;
using YDots.CAM.Application.Features.ReferenceData.Queries;
using YDots.CAM.Infrastructure.Authorization;

namespace YDots.CAM.API.Controllers;

/// <summary>
/// The global reference tables - Channel, Source, Medium - and the enum option lists.
///
/// THESE ROUTES USE <c>ActiveUserOnly</c>, NOT <c>TenantContextRequired</c>, unlike the rest of
/// the module. The reference tables are platform-wide rather than Organisation-owned, so a
/// SuperAdmin doing platform work with no Organisation selected can still read them - and the
/// campaign forms need them before anything Organisation-scoped is loaded.
/// </summary>
[Route("api/v1/campaign-reference")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class ReferenceDataController(ReferenceDataQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// Every dropdown the campaign and tracking asset forms need, in one call.
    ///
    /// One payload rather than three round trips, which is three chances to leave a form
    /// half-populated. The enum lists come from the server too, so the client never hard-codes a
    /// set of values that will drift the first time one is added.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ReferenceView)]
    [ProducesResponseType(typeof(ApiResponse<CampaignReferenceDataResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReferenceDataAsync(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetCampaignReferenceDataQuery(!includeInactive), cancellationToken));

    [HttpGet("channels")]
    [HasPermission(PermissionCodes.ReferenceView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<ReferenceItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChannelsAsync(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetChannelsQuery(!includeInactive), cancellationToken));

    [HttpGet("sources")]
    [HasPermission(PermissionCodes.ReferenceView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<ReferenceItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSourcesAsync(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetSourcesQuery(!includeInactive), cancellationToken));

    [HttpGet("mediums")]
    [HasPermission(PermissionCodes.ReferenceView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<ReferenceItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMediumsAsync(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetMediumsQuery(!includeInactive), cancellationToken));
}
