using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The cross-cutting endpoint the five Masters screens share.
///
/// ONE CALL RATHER THAN FIVE. A City form needs countries, states and time zones before it can
/// be drawn, and a Country form needs currencies and the region list. Five separate lookup
/// calls is five round trips before any of those forms is usable, and five chances for one to
/// fail and leave the form half-populated.
///
/// The enum option lists are served from here too, so the client never hard-codes a set of
/// values that will drift the first time one is added.
/// </summary>
[Route("api/v1/masters")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class MastersController(GlobalMasterQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// Every dropdown the Masters screens need.
    ///
    /// <paramref name="countryId"/> narrows the state list to one country, which is what the
    /// City form wants. Omitting it returns every state, which is what the State grid's own
    /// filter wants.
    ///
    /// GATED ON THE SECTION PERMISSION, not on the five individual view codes. A user who can
    /// open any Masters screen needs the pickers for it, and requiring all five here would
    /// make the shared endpoint unusable for somebody granted only the city list.
    /// </summary>
    [HttpGet("reference-data")]
    [HasPermission(PermissionCodes.GlobalMaster.Section)]
    [ProducesResponseType(
        typeof(ApiResponse<GlobalMasterReferenceDataResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReferenceDataAsync(
        [FromQuery] Guid? countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetGlobalMasterReferenceDataQuery(countryId), cancellationToken));
}
