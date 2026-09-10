using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCity;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The city master.
///
/// See <see cref="CountriesController"/> for why these routes use <c>ActiveUserOnly</c> rather
/// than <c>TenantContextRequired</c>.
/// </summary>
[Route("api/v1/masters/cities")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class CitiesController(
    CityCommandHandler commands,
    GlobalMasterQueryHandler queries,
    ILogger<CitiesController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CityListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] CitySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching cities.");

        var result = await queries.HandleAsync(new SearchCitiesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetCityAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(typeof(ApiResponse<CityDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCityAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting city. CityId: {CityId}", id);

        var result = await queries.HandleAsync(new GetCityQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get city. CityId: {CityId}", id);

        return FromResult(result);
    }

    /// <summary>Active cities beneath one state, for an address form's third dropdown.</summary>
    [HttpGet("lookup/{stateProvinceId:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(
        Guid stateProvinceId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Looking up active cities. StateProvinceId: {StateProvinceId}", stateProvinceId);

        var result = await queries.HandleAsync(new LookupCitiesQuery(stateProvinceId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City lookup failed. StateProvinceId: {StateProvinceId}", stateProvinceId);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] CitySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting cities.");

        var result = await queries.HandleAsync(new ExportCitiesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City export failed.");

        return FileFromResult(result);
    }

    /// <summary>
    /// Adds a city.
    ///
    /// The country is taken from the chosen state and is not part of the request - see
    /// <c>CreateCityRequest</c> for why that is the only way the denormalised column stays
    /// trustworthy.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesCreate)]
    [ProducesResponseType(typeof(ApiResponse<CityDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCityRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating city.");

        var result = await commands.HandleAsync(new CreateCityCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("City creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("City created successfully. CityId: {CityId}", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetCityAsync), new { id = result.Value!.Id }, "City created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateCityRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating city. CityId: {CityId}", id);

        var result = await commands.HandleAsync(new UpdateCityCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City update failed. CityId: {CityId}", id);
        else
            logger.LogInformation("City updated successfully. CityId: {CityId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating city. CityId: {CityId}", id);

        var result = await commands.HandleAsync(
            new ChangeCityStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City activation failed. CityId: {CityId}", id);
        else
            logger.LogInformation("City activated successfully. CityId: {CityId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deactivating city. CityId: {CityId}", id);

        var result = await commands.HandleAsync(
            new ChangeCityStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City deactivation failed. CityId: {CityId}", id);
        else
            logger.LogInformation("City deactivated successfully. CityId: {CityId}", id);

        return FromResult(result);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting city. CityId: {CityId}", id);

        var result = await commands.HandleAsync(new DeleteCityCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("City deletion failed. CityId: {CityId}", id);
        else
            logger.LogInformation("City deleted successfully. CityId: {CityId}", id);

        return FromResult(result);
    }
}