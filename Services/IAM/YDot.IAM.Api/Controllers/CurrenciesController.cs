using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCurrency;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The currency master.
///
/// See <see cref="CountriesController"/> for why these routes use <c>ActiveUserOnly</c> rather
/// than <c>TenantContextRequired</c>.
/// </summary>
[Route("api/v1/masters/currencies")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
public sealed class CurrenciesController(
    CurrencyCommandHandler commands,
    GlobalMasterQueryHandler queries,
    ILogger<CurrenciesController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<CurrencyListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] CurrencySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching currencies.");

        var result = await queries.HandleAsync(new SearchCurrenciesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetCurrencyAsync))]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesView)]
    [ProducesResponseType(typeof(ApiResponse<CurrencyDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrencyAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting currency. CurrencyId: {CurrencyId}", id);

        var result = await queries.HandleAsync(new GetCurrencyQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get currency. CurrencyId: {CurrencyId}", id);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] CurrencySearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting currencies.");

        var result = await queries.HandleAsync(new ExportCurrenciesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency export failed.");

        return FileFromResult(result);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesCreate)]
    [ProducesResponseType(typeof(ApiResponse<CurrencyDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCurrencyRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating currency.");

        var result = await commands.HandleAsync(new CreateCurrencyCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Currency creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Currency created successfully. CurrencyId: {CurrencyId}", result.Value!.Id);

        return CreatedFromResult(
            result, nameof(GetCurrencyAsync), new { id = result.Value!.Id }, "Currency created.");
    }

    /// <summary>
    /// Edits a currency.
    ///
    /// The CODE cannot be changed - it identifies the currency, and repointing INR at another
    /// unit would redenominate every donation that referenced it. Decimal places CAN be
    /// changed, because a currency set up wrongly has to be correctable, and the change is
    /// audited with its previous value.
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateCurrencyRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating currency. CurrencyId: {CurrencyId}", id);

        var result = await commands.HandleAsync(new UpdateCurrencyCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency update failed. CurrencyId: {CurrencyId}", id);
        else
            logger.LogInformation("Currency updated successfully. CurrencyId: {CurrencyId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating currency. CurrencyId: {CurrencyId}", id);

        var result = await commands.HandleAsync(
            new ChangeCurrencyStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Active)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency activation failed. CurrencyId: {CurrencyId}", id);
        else
            logger.LogInformation("Currency activated successfully. CurrencyId: {CurrencyId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Retires a currency.
    ///
    /// NOT blocked by usage, unlike deletion: the response reports how many countries still
    /// name it as their default so the operator knows what they have just affected, but the
    /// change goes through. Refusing here would leave no way to retire anything that had ever
    /// been used.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] MasterStatusChangeRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deactivating currency. CurrencyId: {CurrencyId}", id);

        var result = await commands.HandleAsync(
            new ChangeCurrencyStatusCommand(id, request.ToCommandRequest(MasterDataStatus.Inactive)),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency deactivation failed. CurrencyId: {CurrencyId}", id);
        else
            logger.LogInformation("Currency deactivated successfully. CurrencyId: {CurrencyId}", id);

        return FromResult(result);
    }

    /// <summary>Deletes a currency. Refused while any country names it as their default.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.GlobalMaster.CurrenciesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteMasterRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting currency. CurrencyId: {CurrencyId}", id);

        var result = await commands.HandleAsync(new DeleteCurrencyCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Currency deletion failed. CurrencyId: {CurrencyId}", id);
        else
            logger.LogInformation("Currency deleted successfully. CurrencyId: {CurrencyId}", id);

        return FromResult(result);
    }
}