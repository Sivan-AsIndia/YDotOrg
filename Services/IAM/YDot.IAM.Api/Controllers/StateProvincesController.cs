using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageStateProvince;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The state, province and union-territory master.
///
/// See <see cref="CountriesController"/> for why these routes use <c>ActiveUserOnly</c> rather
/// than <c>TenantContextRequired</c>.
/// </summary>
[Route("api/v1/masters/states")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class StateProvincesController(
    StateProvinceCommandHandler commands,
    GlobalMasterQueryHandler queries,
    ILogger<StateProvincesController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.StatesView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<StateProvinceListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] StateProvinceSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching state provinces.");

        var result = await queries.HandleAsync(new SearchStateProvincesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetStateProvinceAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.StatesView)]
    [ProducesResponseType(typeof(ApiResponse<StateProvinceDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStateProvinceAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting state province. StateProvinceId: {StateProvinceId}", id);

        var result = await queries.HandleAsync(new GetStateProvinceQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get state province. StateProvinceId: {StateProvinceId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Active states beneath one country, for the cascading City form.
    ///
    /// A LOOKUP RATHER THAN THE GRID, because the two answer different questions: the grid
    /// pages and includes retired rows, while a picker wants every selectable option at once
    /// and nothing that cannot be chosen.
    /// </summary>
    [HttpGet("lookup/{countryId:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<MasterLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(Guid countryId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Looking up active state provinces. CountryId: {CountryId}", countryId);

        var result = await queries.HandleAsync(new LookupStateProvincesQuery(countryId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province lookup failed. CountryId: {CountryId}", countryId);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] StateProvinceSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting state provinces.");

        var result = await queries.HandleAsync(new ExportStateProvincesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province export failed.");

        return FileFromResult(result);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.StatesCreate)]
    [ProducesResponseType(typeof(ApiResponse<StateProvinceDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateStateProvinceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating state province.");

        var result = await commands.HandleAsync(
            new CreateStateProvinceCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("State province creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("State province created successfully. StateProvinceId: {StateProvinceId}", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetStateProvinceAsync), new { id = result.Value.Id }, "State created.");
    }

    /// <summary>
    /// Edits a state.
    ///
    /// The country cannot be changed here, and deliberately so - re-parenting a state would
    /// silently rewrite the geography of every address beneath it. See
    /// <c>UpdateStateProvinceRequest</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateStateProvinceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Updating state province. StateProvinceId: {StateProvinceId}", id);

        var result = await commands.HandleAsync(
            new UpdateStateProvinceCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province update failed. StateProvinceId: {StateProvinceId}", id);
        else
            logger.LogInformation("State province updated successfully. StateProvinceId: {StateProvinceId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Activating state province. StateProvinceId: {StateProvinceId}", id);

        var result = await commands.HandleAsync(
            new ChangeStateProvinceStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province activation failed. StateProvinceId: {StateProvinceId}", id);
        else
            logger.LogInformation("State province activated successfully. StateProvinceId: {StateProvinceId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deactivating state province. StateProvinceId: {StateProvinceId}", id);

        var result = await commands.HandleAsync(
            new ChangeStateProvinceStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province deactivation failed. StateProvinceId: {StateProvinceId}", id);
        else
            logger.LogInformation("State province deactivated successfully. StateProvinceId: {StateProvinceId}", id);

        return FromResult(result);
    }

    /// <summary>Deletes a state. Refused while any city sits beneath it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deleting state province. StateProvinceId: {StateProvinceId}", id);

        var result = await commands.HandleAsync(
            new DeleteStateProvinceCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("State province deletion failed. StateProvinceId: {StateProvinceId}", id);
        else
            logger.LogInformation("State province deleted successfully. StateProvinceId: {StateProvinceId}", id);

        return FromResult(result);
    }
}