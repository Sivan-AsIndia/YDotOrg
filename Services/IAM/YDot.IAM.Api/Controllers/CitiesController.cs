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
    GlobalMasterQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CityListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] CitySearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchCitiesQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetCityAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(typeof(ApiResponse<CityDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCityAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetCityQuery(id), cancellationToken));

    /// <summary>Active cities beneath one state, for an address form's third dropdown.</summary>
    [HttpGet("lookup/{stateProvinceId:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<MasterLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(
        Guid stateProvinceId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupCitiesQuery(stateProvinceId), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] CitySearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportCitiesQuery(filter), cancellationToken));

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
        var result = await commands.HandleAsync(new CreateCityCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetCityAsync), new { id = result.Value!.Id }, "City created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateCityRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new UpdateCityCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeCityStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeCityStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CitiesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new DeleteCityCommand(id, request), cancellationToken));
}
