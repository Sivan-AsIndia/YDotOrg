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
    GlobalMasterQueryHandler queries,
    ILogger<TimeZonesController> logger) : ApiControllerBase
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
        [FromQuery] TimeZoneSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching time zones.");

        var result = await queries.HandleAsync(new SearchTimeZonesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetTimeZoneAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesView)]
    [ProducesResponseType(typeof(ApiResponse<TimeZoneDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeZoneAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting time zone. TimeZoneId: {TimeZoneId}", id);

        var result = await queries.HandleAsync(new GetTimeZoneQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get time zone. TimeZoneId: {TimeZoneId}", id);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] TimeZoneSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting time zones.");

        var result = await queries.HandleAsync(new ExportTimeZonesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone export failed.");

        return FileFromResult(result);
    }

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
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating time zone.");

        var result = await commands.HandleAsync(new CreateTimeZoneCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Time zone creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Time zone created successfully. TimeZoneId: {TimeZoneId}", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetTimeZoneAsync), new { id = result.Value.Id }, "Time zone created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateTimeZoneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Updating time zone. TimeZoneId: {TimeZoneId}", id);

        var result = await commands.HandleAsync(
            new UpdateTimeZoneCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone update failed. TimeZoneId: {TimeZoneId}", id);
        else
            logger.LogInformation("Time zone updated successfully. TimeZoneId: {TimeZoneId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Activating time zone. TimeZoneId: {TimeZoneId}", id);

        var result = await commands.HandleAsync(
            new ChangeTimeZoneStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone activation failed. TimeZoneId: {TimeZoneId}", id);
        else
            logger.LogInformation("Time zone activated successfully. TimeZoneId: {TimeZoneId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deactivating time zone. TimeZoneId: {TimeZoneId}", id);

        var result = await commands.HandleAsync(
            new ChangeTimeZoneStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone deactivation failed. TimeZoneId: {TimeZoneId}", id);
        else
            logger.LogInformation("Time zone deactivated successfully. TimeZoneId: {TimeZoneId}", id);

        return FromResult(result);
    }

    /// <summary>Deletes a time zone. Refused while any state defaults to it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.TimeZonesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deleting time zone. TimeZoneId: {TimeZoneId}", id);

        var result = await commands.HandleAsync(
            new DeleteTimeZoneCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Time zone deletion failed. TimeZoneId: {TimeZoneId}", id);
        else
            logger.LogInformation("Time zone deleted successfully. TimeZoneId: {TimeZoneId}", id);

        return FromResult(result);
    }
}