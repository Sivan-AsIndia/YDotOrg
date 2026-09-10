using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCountry;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The country master, migrated in from the standalone GlobalMaster service.
///
/// THE POLICY IS <c>ActiveUserOnly</c> AND NOT <c>TenantContextRequired</c>, which is the one
/// thing that distinguishes these routes from the rest of IAM. The master catalogue is shared:
/// a Tenant user reads the platform rows plus their own, and SuperAdmin maintains the platform
/// rows while operating in NO Organisation at all. Requiring a resolved Tenant would lock the
/// root user out of the very screens they own.
///
/// Isolation is not weakened by that. The scoped query filter still applies to every read, and
/// <c>GlobalMasterWriteGuard</c> still refuses a Tenant caller any write against a platform row.
/// </summary>
[Route("api/v1/masters/countries")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class CountriesController(
    CountryCommandHandler commands,
    GlobalMasterQueryHandler queries,
    ILogger<CountriesController> logger) : ApiControllerBase
{
    /// <summary>The country grid: platform rows plus the caller's own.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CountryListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] CountrySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching countries.");

        var result = await queries.HandleAsync(new SearchCountriesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetCountryAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesView)]
    [ProducesResponseType(typeof(ApiResponse<CountryDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCountryAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting country. CountryId: {CountryId}", id);

        var result = await queries.HandleAsync(new GetCountryQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get country. CountryId: {CountryId}", id);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] CountrySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting countries.");

        var result = await queries.HandleAsync(new ExportCountriesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country export failed.");

        return FileFromResult(result);
    }

    /// <summary>
    /// Adds a country.
    ///
    /// It lands in whichever Organisation the caller is operating in - or in the shared
    /// platform catalogue when a root user is operating in none. The request has no field for
    /// choosing.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesCreate)]
    [ProducesResponseType(typeof(ApiResponse<CountryDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCountryRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating country.");

        var result = await commands.HandleAsync(new CreateCountryCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Country creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Country created successfully. CountryId: {CountryId}", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetCountryAsync), new { id = result.Value!.Id }, "Country created.");
    }

    /// <summary>Edits a country. A platform row is refused for anybody but SuperAdmin.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateCountryRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating country. CountryId: {CountryId}", id);

        var result = await commands.HandleAsync(new UpdateCountryCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country update failed. CountryId: {CountryId}", id);
        else
            logger.LogInformation("Country updated successfully. CountryId: {CountryId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Activates a country.
    ///
    /// A SEPARATE ROUTE FROM DEACTIVATE, and separately permissioned, because the two are not
    /// equally consequential: switching a country back on is routine, while switching it off
    /// removes it from every address form in the Organisation.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating country. CountryId: {CountryId}", id);

        var result = await commands.HandleAsync(
            new ChangeCountryStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country activation failed. CountryId: {CountryId}", id);
        else
            logger.LogInformation("Country activated successfully. CountryId: {CountryId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deactivating country. CountryId: {CountryId}", id);

        var result = await commands.HandleAsync(
            new ChangeCountryStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country deactivation failed. CountryId: {CountryId}", id);
        else
            logger.LogInformation("Country deactivated successfully. CountryId: {CountryId}", id);

        return FromResult(result);
    }

    /// <summary>Deletes a country. Refused while any state or city sits beneath it.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CountriesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting country. CountryId: {CountryId}", id);

        var result = await commands.HandleAsync(new DeleteCountryCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Country deletion failed. CountryId: {CountryId}", id);
        else
            logger.LogInformation("Country deleted successfully. CountryId: {CountryId}", id);

        return FromResult(result);
    }
}