using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.ReferenceData.Queries;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The dropdown data every IAM form needs, in one call.
///
/// ONE CALL, NOT SIX. A form that opens with six parallel requests has six chances to render
/// half-populated, and the Angular side then has to sequence them. This returns roles,
/// departments, units, managers, permissions and selectable Organisations together.
///
/// Everything is already Organisation-scoped by the query filter, so a department belonging to
/// another Organisation cannot appear in the list — and therefore cannot be picked.
/// </summary>
[Route("api/v1/reference-data")]
[Authorize]
public sealed class ReferenceDataController(ReferenceDataQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ReferenceDataResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetReferenceDataQuery(), cancellationToken));

    /// <summary>
    /// Every enumeration the UI renders as a dropdown, with display labels.
    ///
    /// Served from the API rather than duplicated in TypeScript so the two cannot drift: adding
    /// a status to the domain makes it appear in the UI without an Angular change.
    /// </summary>
    [HttpGet("enums")]
    [ProducesResponseType(typeof(ApiResponse<EnumOptionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEnumsAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetEnumOptionsQuery(), cancellationToken));
}
