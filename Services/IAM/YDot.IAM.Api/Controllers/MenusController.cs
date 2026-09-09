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
    NavigationQueryHandler queries,
    ILogger<MenusController> logger) : ApiControllerBase
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
    public async Task<IActionResult> GetNavigationAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting navigation tree.");

        var result = await queries.HandleAsync(new GetNavigationQuery(), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Navigation tree retrieval failed.");

        return FromResult(result);
    }

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
        [FromQuery] bool includePlatformNodes, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Getting menu catalogue. IncludePlatformNodes: {IncludePlatformNodes}",
            includePlatformNodes);

        var result = await queries.HandleAsync(
            new GetMenuCatalogueQuery(includePlatformNodes), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning(
                "Menu catalogue retrieval failed. IncludePlatformNodes: {IncludePlatformNodes}",
                includePlatformNodes);

        return FromResult(result);
    }

    /// <summary>What this Organisation has enabled, node by node.</summary>
    [HttpGet("configuration")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<TenantMenuConfigurationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting tenant menu configuration.");

        var result = await queries.HandleAsync(
            new GetTenantMenuConfigurationQuery(), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Tenant menu configuration retrieval failed.");

        return FromResult(result);
    }

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
        [FromBody] ConfigureTenantMenuRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Configuring tenant menu.");

        var result = await commands.HandleAsync(
            new ConfigureTenantMenuCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Tenant menu configuration failed.");
        else
            logger.LogInformation("Tenant menu configured successfully.");

        return FromResult(result);
    }

    /// <summary>The menu-to-role mapping screen.</summary>
    [HttpGet("role-mapping/{roleId:guid}")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<RoleMenuMappingResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoleMappingAsync(
        Guid roleId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting role menu mapping. RoleId: {RoleId}", roleId);

        var result = await queries.HandleAsync(
            new GetRoleMenuMappingQuery(roleId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role menu mapping retrieval failed. RoleId: {RoleId}", roleId);

        return FromResult(result);
    }

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
        Guid roleId, [FromBody] MapRoleMenusRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mapping menus to role. RoleId: {RoleId}", roleId);

        var result = await commands.HandleAsync(
            new MapRoleMenusCommand(roleId, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Role menu mapping failed. RoleId: {RoleId}", roleId);
        else
            logger.LogInformation("Role menus mapped successfully. RoleId: {RoleId}", roleId);

        return FromResult(result);
    }

    // ---- Platform catalogue authoring (SuperAdmin) --------------------------------------------

    /// <summary>
    /// The catalogue in full, for the authoring screen.
    ///
    /// Separate from <c>GET catalogue</c> because that one returns what the SIDEBAR needs and
    /// carries no version, status, description, parent id or flags — nothing you could edit
    /// against. Gated on the platform manage permission rather than the view permission,
    /// because the fields it adds are only useful to somebody authoring.
    /// </summary>
    [HttpGet("definitions")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MenuDefinitionResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefinitionsAsync(
        [FromQuery] bool includeRetired, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Getting menu definitions. IncludeRetired: {IncludeRetired}",
            includeRetired);

        var result = await queries.HandleAsync(
            new GetMenuDefinitionsQuery(includeRetired), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning(
                "Menu definitions retrieval failed. IncludeRetired: {IncludeRetired}",
                includeRetired);

        return FromResult(result);
    }

    /// <summary>
    /// The permission codes a node may be gated on, for the authoring picker.
    ///
    /// <c>GET /permissions</c> would be the obvious home for this and cannot be: it is gated on
    /// the TENANT permission <c>iam.permissions.view</c>, which a SuperAdmin at platform level
    /// does not hold — leaving the picker empty for the only person who uses this screen.
    /// </summary>
    // READABLE BY ANY MENU ADMINISTRATOR. It is the list of permission codes a node can be
    // guarded by - the picker behind "who is allowed to see this item" - and an Organisation
    // building its own menu needs it as much as the platform does. The codes are not secrets:
    // they are already on every role screen.
    [HttpGet("definitions/permission-codes")]
    [HasPermission(PermissionCodes.MenusView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MenuPermissionOptionResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionCodesAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting menu permission codes.");

        var result = await queries.HandleAsync(
            new GetMenuPermissionCodesQuery(), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Menu permission codes retrieval failed.");

        return FromResult(result);
    }

    /// <summary>
    /// Adds a node to the PLATFORM catalogue — a new product feature, available to every
    /// Organisation. Not a per-Organisation setting; that is <c>PUT configuration</c>.
    /// </summary>
    // THE ATTRIBUTE IS THE COARSE GATE, THE HANDLER IS THE RULE. Either permission can reach
    // these three endpoints now, because either kind of caller has nodes they are entitled to
    // write - a platform administrator the catalogue, an Organisation its own. Which set this
    // particular caller may touch is decided in MenuCommandHandler against the ROW, which is
    // the only check that can tell those two apart. A SuperAdmin satisfies any permission check
    // by scope, so gating on the tenant code costs the platform caller nothing.
    [HttpPost("definitions")]
    [HasPermission(PermissionCodes.MenusManageStructure)]
    [ProducesResponseType(typeof(ApiResponse<MenuDefinitionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDefinitionAsync(
        [FromBody] CreateMenuDefinitionRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating menu definition.");

        var result = await commands.HandleAsync(
            new CreateMenuDefinitionCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Menu definition creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Menu definition created successfully.");

        return FromResult(result);
    }

    [HttpPut("definitions/{menuId:guid}")]
    [HasPermission(PermissionCodes.MenusManageStructure)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDefinitionAsync(
        Guid menuId, [FromBody] UpdateMenuDefinitionRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating menu definition. MenuId: {MenuId}", menuId);

        var result = await commands.HandleAsync(
            new UpdateMenuDefinitionCommand(menuId, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Menu definition update failed. MenuId: {MenuId}", menuId);
        else
            logger.LogInformation("Menu definition updated successfully. MenuId: {MenuId}", menuId);

        return FromResult(result);
    }

    /// <summary>
    /// Removes a node from the catalogue.
    ///
    /// REFUSED THE MOMENT ANYTHING DEPENDS ON IT — children, or any Organisation configuration
    /// or role mapping that points at it — because deleting the row those point at leaves
    /// orphans across every Organisation. The answer in that case is to RETIRE it through the
    /// update endpoint, which hides it everywhere, keeps the history and can be undone.
    ///
    /// This is for the node somebody added by mistake and nothing has touched yet.
    /// </summary>
    [HttpDelete("definitions/{menuId:guid}")]
    [HasPermission(PermissionCodes.MenusManageStructure)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteDefinitionAsync(
        Guid menuId, [FromQuery] long expectedVersion, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Deleting menu definition. MenuId: {MenuId}, ExpectedVersion: {ExpectedVersion}",
            menuId, expectedVersion);

        var result = await commands.HandleAsync(
            new DeleteMenuDefinitionCommand(menuId, expectedVersion), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning(
                "Menu definition deletion failed. MenuId: {MenuId}, ExpectedVersion: {ExpectedVersion}",
                menuId, expectedVersion);
        else
            logger.LogInformation("Menu definition deleted successfully. MenuId: {MenuId}", menuId);

        return FromResult(result);
    }
}