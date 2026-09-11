using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Roles.Queries.RoleQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The permission catalogue: every code the product defines.
///
/// WHY THIS IS NOT ON <c>RolesController</c> ANY MORE. It used to be, and that controller carries
/// <c>TenantContextRequired</c> at class level - correctly, because roles belong to one
/// Organisation. The catalogue does not: <c>Permission</c> is platform data with no TenantId, and
/// the SuperAdmin's Permission Catalogue screen reads it at platform scope, with no Organisation
/// selected. Class-level policies cannot be relaxed per action, so every visit to that screen was
/// refused with 403 before the handler ran.
///
/// WHAT STILL NEEDS AN ORGANISATION. The matrix for ONE ROLE - <c>?roleId=</c>, which the role
/// editor asks for - is refused by the handler without one, exactly as it was before the move.
/// Only the role-free catalogue became reachable from the platform.
/// </summary>
[Route("api/v1/permissions")]
[Authorize]
public sealed class PermissionsController(
    RoleQueryHandler queries,
    ILogger<PermissionsController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PermissionListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchPermissionsAsync(
        [FromQuery] PermissionSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching permissions.");

        var result = await queries.HandleAsync(new SearchPermissionsQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Permission search failed.");

        return FromResult(result);
    }

    /// <summary>
    /// The permission matrix, grouped by module and group.
    ///
    /// Grouped because a flat list of a hundred and thirty codes is unusable and the point of the
    /// screen is to let somebody reason about what a role can do.
    /// </summary>
    [HttpGet("matrix")]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<PermissionMatrixResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPermissionMatrixAsync(
        [FromQuery] Guid? roleId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting permission matrix. RoleId: {RoleId}", roleId);

        var result = await queries.HandleAsync(
            new GetPermissionMatrixQuery(roleId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get permission matrix. RoleId: {RoleId}", roleId);

        return FromResult(result);
    }
}
