using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Roles.Commands.ManageRole;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Roles.Queries.RoleQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Roles. The permission catalogue is on <see cref="PermissionsController"/>.
///
/// ROLES ARE TENANT-SPECIFIC, so every route here is scoped by the token. Two Organisations
/// may both have a role coded ADMIN and neither can see the other.
/// </summary>
[Route("api/v1/roles")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class RolesController(
    RoleCommandHandler commands,
    RoleQueryHandler queries,
    ILogger<RolesController> logger) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<RoleListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] RoleSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching roles.");

        var result = await queries.HandleAsync(new SearchRolesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetRoleAsync))]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoleAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting role. RoleId: {RoleId}", id);

        var result = await queries.HandleAsync(new GetRoleQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get role. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RoleLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Looking up roles.");

        var result = await queries.HandleAsync(new LookupRolesQuery(), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role lookup failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}/members")]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<RoleMemberResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembersAsync(
        Guid id, [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting role members. RoleId: {RoleId}", id);

        var result = await queries.HandleAsync(new GetRoleMembersQuery(id, pagination), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get role members. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    [HttpGet("export")]
    [HasPermission(PermissionCodes.RolesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] RoleSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting roles.");

        var result = await queries.HandleAsync(new ExportRolesQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role export failed.");

        return FileFromResult(result);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.RolesCreate)]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating role.");

        var result = await commands.HandleAsync(new CreateRoleCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Role creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Role created successfully. RoleId: {RoleId}", result.Value!.Id);

        return CreatedFromResult(result, nameof(GetRoleAsync), new { id = result.Value.Id }, "Role created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RolesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Updating role. RoleId: {RoleId}", id);

        var result = await commands.HandleAsync(new UpdateRoleCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role update failed. RoleId: {RoleId}", id);
        else
            logger.LogInformation("Role updated successfully. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Replaces a role permission set.
    ///
    /// A platform-only code is REFUSED here, not silently dropped — an administrator who was
    /// quietly given less than they asked for would believe they had granted something they
    /// had not.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(PermissionCodes.RolesAssignPermissions)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignPermissionsAsync(
        Guid id, [FromBody] AssignRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Assigning permissions to role. RoleId: {RoleId}", id);

        var result = await commands.HandleAsync(
            new AssignRolePermissionsCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role permission assignment failed. RoleId: {RoleId}", id);
        else
            logger.LogInformation("Role permissions assigned successfully. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    [HttpPut("{id:guid}/claims")]
    [HasPermission(PermissionCodes.RolesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignClaimsAsync(
        Guid id, [FromBody] AssignRoleClaimsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Assigning claims to role. RoleId: {RoleId}", id);

        var result = await commands.HandleAsync(
            new AssignRoleClaimsCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role claim assignment failed. RoleId: {RoleId}", id);
        else
            logger.LogInformation("Role claims assigned successfully. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.RolesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid id, [FromBody] ChangeRoleStatusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Changing role status. RoleId: {RoleId}", id);

        var result = await commands.HandleAsync(
            new ChangeRoleStatusCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role status change failed. RoleId: {RoleId}", id);
        else
            logger.LogInformation("Role status changed successfully. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    /// <summary>Deletes a role. Refused when anybody holds it, or when it is a system role.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.RolesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deleting role. RoleId: {RoleId}", id);

        var result = await commands.HandleAsync(
            new DeleteRoleCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role deletion failed. RoleId: {RoleId}", id);
        else
            logger.LogInformation("Role deleted successfully. RoleId: {RoleId}", id);

        return FromResult(result);
    }

    // ---- Segregation of duties -----------------------------------------------------------

    [HttpPost("incompatibilities")]
    [HasPermission(PermissionCodes.RolesManageIncompatibility)]
    [ProducesResponseType(typeof(ApiResponse<RoleIncompatibilityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateIncompatibilityAsync(
        [FromBody] CreateRoleIncompatibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating role incompatibility.");

        var result = await commands.HandleAsync(
            new CreateRoleIncompatibilityCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role incompatibility creation failed.");
        else
            logger.LogInformation("Role incompatibility created successfully.");

        return FromResult(result);
    }

    [HttpDelete("incompatibilities/{id:guid}")]
    [HasPermission(PermissionCodes.RolesManageIncompatibility)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteIncompatibilityAsync(
        Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting role incompatibility. IncompatibilityId: {IncompatibilityId}", id);

        var result = await commands.HandleAsync(
            new DeleteRoleIncompatibilityCommand(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role incompatibility deletion failed. IncompatibilityId: {IncompatibilityId}", id);
        else
            logger.LogInformation("Role incompatibility deleted successfully. IncompatibilityId: {IncompatibilityId}", id);

        return FromResult(result);
    }

    // The permission catalogue (GET /api/v1/permissions and /permissions/matrix) moved to
    // PermissionsController. It is platform data, and this controller's TenantContextRequired
    // policy refused it to the SuperAdmin's Permission Catalogue screen at platform scope.
}