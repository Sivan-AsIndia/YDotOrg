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
/// Roles and the permission catalogue.
///
/// ROLES ARE TENANT-SPECIFIC, so every route here is scoped by the token. Two Organisations
/// may both have a role coded ADMIN and neither can see the other.
/// </summary>
[Route("api/v1/roles")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class RolesController(
    RoleCommandHandler commands,
    RoleQueryHandler queries) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<RoleListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] RoleSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchRolesQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetRoleAsync))]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoleAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetRoleQuery(id), cancellationToken));

    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RoleLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupRolesQuery(), cancellationToken));

    [HttpGet("{id:guid}/members")]
    [HasPermission(PermissionCodes.RolesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<RoleMemberResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembersAsync(
        Guid id, [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetRoleMembersQuery(id, pagination), cancellationToken));

    [HttpGet("export")]
    [HasPermission(PermissionCodes.RolesExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] RoleSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportRolesQuery(filter), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.RolesCreate)]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await commands.HandleAsync(new CreateRoleCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(result, nameof(GetRoleAsync), new { id = result.Value!.Id }, "Role created.");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RolesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new UpdateRoleCommand(id, request), cancellationToken));

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
        Guid id, [FromBody] AssignRolePermissionsRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new AssignRolePermissionsCommand(id, request), cancellationToken));

    [HttpPut("{id:guid}/claims")]
    [HasPermission(PermissionCodes.RolesEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignClaimsAsync(
        Guid id, [FromBody] AssignRoleClaimsRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new AssignRoleClaimsCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.RolesActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid id, [FromBody] ChangeRoleStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new ChangeRoleStatusCommand(id, request), cancellationToken));

    /// <summary>Deletes a role. Refused when anybody holds it, or when it is a system role.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.RolesDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromBody] DeleteRoleRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new DeleteRoleCommand(id, request), cancellationToken));

    // ---- Segregation of duties -----------------------------------------------------------

    [HttpPost("incompatibilities")]
    [HasPermission(PermissionCodes.RolesManageIncompatibility)]
    [ProducesResponseType(typeof(ApiResponse<RoleIncompatibilityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateIncompatibilityAsync(
        [FromBody] CreateRoleIncompatibilityRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new CreateRoleIncompatibilityCommand(request), cancellationToken));

    [HttpDelete("incompatibilities/{id:guid}")]
    [HasPermission(PermissionCodes.RolesManageIncompatibility)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteIncompatibilityAsync(
        Guid id, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new DeleteRoleIncompatibilityCommand(id), cancellationToken));

    // ---- Permission catalogue ---------------------------------------------------------------

    [HttpGet("/api/v1/permissions")]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PermissionListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchPermissionsAsync(
        [FromQuery] PermissionSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchPermissionsQuery(filter), cancellationToken));

    /// <summary>
    /// The permission matrix the role editor renders.
    ///
    /// Grouped by module and group, because a flat list of a hundred and thirty codes is
    /// unusable and the point of the screen is to let somebody reason about what a role can do.
    /// </summary>
    [HttpGet("/api/v1/permissions/matrix")]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<PermissionMatrixResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionMatrixAsync(
        [FromQuery] Guid? roleId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetPermissionMatrixQuery(roleId), cancellationToken));
}
