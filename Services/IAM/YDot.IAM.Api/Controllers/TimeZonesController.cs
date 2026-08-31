using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageTimeZone;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The time-zone master.
///
/// See <see cref="CountriesController"/> for why these routes use <c>ActiveUserOnly</c> rather
/// than <c>TenantContextRequired</c>.
/// </summary>
[Route("api/v1/masters/timezones")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class TimeZonesController(
    TimeZoneCommandHandler commands,
    GlobalMasterQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// The time-zone grid.
    ///
    /// Ordered by UTC OFFSET by default rather than alphabetically, because that is the order
    /// a zone list is actually read in.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<TimeZoneListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] TimeZoneSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchTimeZonesQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetTimeZoneAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesView)]
    [ProducesResponseType(typeof(ApiResponse<TimeZoneDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeZoneAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetTimeZoneQuery(id), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] TimeZoneSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportTimeZonesQuery(filter), cancellationToken));

    /// <summary>
    /// Adds a time zone.
    ///
    /// The stored Code is derived from the IANA key, so the caller supplies the key and
    /// nothing else identifying.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesCreate)]
    [ProducesResponseType(typeof(ApiResponse<TimeZoneDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateTimeZoneRequest request, CancellationToken cancellationToken)
    {
        var result = await commands.HandleAsync(new CreateTimeZoneCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetTimeZoneAsync), new { id = result.Value!.Id }, "Time zone created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateTimeZoneRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new UpdateTimeZoneCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeTimeZoneStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeTimeZoneStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken));

    /// <summary>Deletes a time zone. Refused while any state defaults to it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new DeleteTimeZoneCommand(id, request), cancellationToken));
}
