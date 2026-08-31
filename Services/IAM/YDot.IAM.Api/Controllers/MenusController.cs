using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Menus.Commands.ManageMenu;
using YDot.IAM.Application.Features.Menus.DTOs;
using YDot.IAM.Application.Features.Menus.Queries.Navigation;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Navigation. Three levels — Menu, Submenu, ChildSubMenu — and every level is data, never a
/// hardcoded array in the Angular app.
///
/// THE MENU A PERSON SEES IS DERIVED, NOT STORED:
/// <code>
///   platform catalogue          what the product HAS
///     ∩ organisation config     what this Organisation TURNED ON
///     ∩ role mapping            what this role was GIVEN
///     ∩ held permissions        what this person may actually DO
///     → the tree returned by /navigation
/// </code>
/// Empty groups are pruned on the way out, so nobody is shown a parent that opens onto nothing.
/// </summary>
[Route("api/v1/menus")]
[Authorize]
public sealed class MenusController(
    MenuCommandHandler commands,
    NavigationQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// The navigation tree for whoever is calling, already filtered.
    ///
    /// Angular renders this verbatim. It is deliberately NOT an authorisation decision — every
    /// endpoint still checks its own permission, so a hand-typed URL gets nowhere.
    /// </summary>
    [HttpGet("/api/v1/navigation")]
    // THE SIDEBAR ITSELF MUST LOAD BEFORE APPROVAL. Without this the shell cannot draw a menu
    // at all during onboarding and shows "Menu unavailable", which strands the administrator on
    // whatever page they happen to be on with no way to navigate. The tree this returns is
    // already filtered down to the onboarding nodes by MenuBuilderService, so allowing the call
    // does not expose anything the gate is there to hide.
    //
    // NOTE FOR ANYONE ADDING AN EXCEPTION: this is the endpoint the Angular client actually
    // calls. AuthController exposes the same data at /api/v1/auth/navigation as an alias, and
    // marking only that one - which is what happened first - fixes nothing.
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<NavigationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNavigationAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetNavigationQuery(), cancellationToken));

    /// <summary>
    /// The full catalogue of navigation nodes the platform defines.
    ///
    /// Platform-only nodes are included only for a Global-scope caller; a TenantAdmin
    /// configuring their own menu never learns those nodes exist.
    /// </summary>
    [HttpGet("catalogue")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MenuNode>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogueAsync(
        [FromQuery] bool includePlatformNodes, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetMenuCatalogueQuery(includePlatformNodes), cancellationToken));

    /// <summary>What this Organisation has enabled, node by node.</summary>
    [HttpGet("configuration")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<TenantMenuConfigurationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigurationAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetTenantMenuConfigurationQuery(), cancellationToken));

    /// <summary>
    /// Turns navigation nodes on or off for this Organisation, and re-orders them.
    ///
    /// Disabling a parent disables its children — leaving a reachable child under a hidden
    /// parent would be a hole, not a convenience.
    /// </summary>
    [HttpPut("configuration")]
    [HasPermission(PermissionCodes.MenusConfigure)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfigureAsync(
        [FromBody] ConfigureTenantMenuRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new ConfigureTenantMenuCommand(request), cancellationToken));

    /// <summary>The menu-to-role mapping screen.</summary>
    [HttpGet("role-mapping/{roleId:guid}")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<RoleMenuMappingResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoleMappingAsync(Guid roleId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetRoleMenuMappingQuery(roleId), cancellationToken));

    /// <summary>
    /// Maps navigation nodes to a role.
    ///
    /// A node that the Organisation has not enabled cannot be mapped — the mapping would be
    /// dead weight and would come back to confuse whoever read it next.
    /// </summary>
    [HttpPut("role-mapping/{roleId:guid}")]
    [HasPermission(PermissionCodes.MenusMapRoles)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MapRoleMenusAsync(
        Guid roleId, [FromBody] MapRoleMenusRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new MapRoleMenusCommand(roleId, request), cancellationToken));

    // ---- Platform catalogue authoring (SuperAdmin) --------------------------------------------

    /// <summary>
    /// Adds a node to the PLATFORM catalogue — a new product feature, available to every
    /// Organisation. Not a per-Organisation setting; that is <c>PUT configuration</c>.
    /// </summary>
    [HttpPost("definitions")]
    [HasPermission(PermissionCodes.Platform.MenuCatalogueManage)]
    [ProducesResponseType(typeof(ApiResponse<MenuDefinitionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDefinitionAsync(
        [FromBody] CreateMenuDefinitionRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(new CreateMenuDefinitionCommand(request), cancellationToken));

    [HttpPut("definitions/{menuId:guid}")]
    [HasPermission(PermissionCodes.Platform.MenuCatalogueManage)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDefinitionAsync(
        Guid menuId, [FromBody] UpdateMenuDefinitionRequest request, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new UpdateMenuDefinitionCommand(menuId, request), cancellationToken));
}
