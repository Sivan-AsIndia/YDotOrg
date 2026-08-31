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
    GlobalMasterQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.StatesView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<StateProvinceListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] StateProvinceSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchStateProvincesQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetStateProvinceAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.StatesView)]
    [ProducesResponseType(typeof(ApiResponse<StateProvinceDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStateProvinceAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetStateProvinceQuery(id), cancellationToken));

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
    public async Task<IActionResult> LookupAsync(Guid countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupStateProvincesQuery(countryId), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] StateProvinceSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportStateProvincesQuery(filter), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.StatesCreate)]
    [ProducesResponseType(typeof(ApiResponse<StateProvinceDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateStateProvinceRequest request, CancellationToken cancellationToken)
    {
        var result = await commands.HandleAsync(
            new CreateStateProvinceCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetStateProvinceAsync), new { id = result.Value!.Id }, "State created.");
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
        Guid id, [FromBody] UpdateStateProvinceRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new UpdateStateProvinceCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeStateProvinceStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangeStateProvinceStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken));

    /// <summary>Deletes a state. Refused while any city sits beneath it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.StatesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new DeleteStateProvinceCommand(id, request), cancellationToken));
}
