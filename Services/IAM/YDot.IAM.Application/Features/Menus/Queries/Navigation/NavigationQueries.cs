using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Features.Menus.DTOs;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Menus.Queries.Navigation;

/// <summary>The navigation the signed-in caller should render.</summary>
public sealed record GetNavigationQuery;

/// <summary>The whole catalogue as a tree, for the configuration screens.</summary>
public sealed record GetMenuCatalogueQuery(bool IncludePlatformNodes = false);

/// <summary>The Organisation menu configuration screen.</summary>
public sealed record GetTenantMenuConfigurationQuery;

/// <summary>The menu-mapping screen for one role.</summary>
public sealed record GetRoleMenuMappingQuery(Guid RoleId);

/// <summary>
/// The read side of the navigation.
///
/// <see cref="GetNavigationQuery"/> is called once by the client shell after sign-in and again
/// after every Organisation switch. Everything it returns has already been through the six
/// filters in <c>IMenuBuilderService</c>, so the client renders what it is given and has no
/// authorisation decision of its own to make.
/// </summary>
public sealed class NavigationQueryHandler(
    IMenuBuilderService menuBuilder,
    IMenuRepository menus,
    IRoleRepository roles,
    ITenantRepository tenants,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
{
    public async Task<Result<NavigationResponse>> HandleAsync(
        GetNavigationQuery query, CancellationToken cancellationToken)
    {
        var menu = await menuBuilder.BuildForCurrentUserAsync(cancellationToken);

        // The landing route is read OFF the tree just built rather than built again. This used
        // to call ResolveLandingRouteAsync, which assembles the whole tree a second time, so
        // the one request every screen makes first did all of its work twice.
        var landing = menuBuilder.ResolveLandingRoute(menu);

        return Result.Success(new NavigationResponse(
            menu,
            landing,
            tenantContext.TenantId,
            tenantContext.TenantName,
            tenantContext.Scope,
            tenantContext.IsTenantMode,
            currentUser.IsSuperAdmin));
    }

    public async Task<Result<IReadOnlyList<MenuNode>>> HandleAsync(
        GetMenuCatalogueQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Platform nodes are only ever included for a root user, whatever the flag asks for.
        var includePlatform = query.IncludePlatformNodes && currentUser.IsSuperAdmin;

        var tree = await menuBuilder.BuildCatalogueAsync(
            tenantContext.TenantId, includePlatform, cancellationToken);

        return Result.Success(tree);
    }

    /// <summary>
    /// The Organisation configuration screen: every catalogue node with this Organisation
    /// decision beside it, so an administrator sees what they have AND what they could have.
    /// </summary>
    public async Task<Result<TenantMenuConfigurationResponse>> HandleAsync(
        GetTenantMenuConfigurationQuery query, CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return Result.Failure<TenantMenuConfigurationResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<TenantMenuConfigurationResponse>(Error.TenantNotFound());
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var tenantMenus = await menus.GetTenantMenusAsync(tenantId, cancellationToken);
        var overridesById = tenantMenus.ToDictionary(item => item.MenuDefinitionId);

        // The platform branch is not an Organisation decision, so it is excluded entirely
        // rather than shown disabled.
        var configurable = catalogue
            .Where(node => !node.IsPlatformOnly && node.Status != MenuStatus.Retired)
            .ToList();

        return Result.Success(new TenantMenuConfigurationResponse(
            tenantId,
            tenant.Name,
            BuildConfigurationTree(configurable, overridesById, parentId: null)));
    }

    /// <summary>
    /// The role mapping screen.
    ///
    /// <c>IsPermitted</c> is what makes this screen honest: a node whose permission the role
    /// does not hold is shown with the checkbox disabled, because ticking it would achieve
    /// nothing — the endpoint behind the screen would still answer 403.
    /// </summary>
    public async Task<Result<RoleMenuMappingResponse>> HandleAsync(
        GetRoleMenuMappingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var role = await roles.GetWithPermissionsAsync(query.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleMenuMappingResponse>(Error.NotFound("That role was not found."));
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var roleMenus = await menus.GetRoleMenusAsync(role.Id, cancellationToken);
        var mappingsById = roleMenus.ToDictionary(mapping => mapping.MenuDefinitionId);

        var held = role.GrantsAllTenantPermissions
            ? null
            : role.RolePermissions
                .Where(grant => !grant.IsDenied)
                .Select(grant => grant.PermissionCode)
                .ToHashSet(StringComparer.Ordinal);

        var configurable = catalogue
            .Where(node => !node.IsPlatformOnly && node.Status != MenuStatus.Retired)
            .ToList();

        var landingMenuId = roleMenus
            .Where(mapping => mapping.IsLandingPage)
            .Select(mapping => (Guid?)mapping.MenuDefinitionId)
            .FirstOrDefault();

        return Result.Success(new RoleMenuMappingResponse(
            role.Id,
            role.Name ?? role.Code,
            landingMenuId,
            BuildMappingTree(configurable, mappingsById, held, parentId: null)));
    }

    private static List<TenantMenuNodeResponse> BuildConfigurationTree(
        IReadOnlyList<Domain.Entities.MenuDefinition> nodes,
        IReadOnlyDictionary<Guid, Domain.Entities.TenantMenu> overridesById,
        Guid? parentId)
    {
        var result = new List<TenantMenuNodeResponse>();

        foreach (var node in nodes.Where(item => item.ParentMenuId == parentId))
        {
            overridesById.TryGetValue(node.Id, out var tenantMenu);

            var children = BuildConfigurationTree(nodes, overridesById, node.Id);

            result.Add(new TenantMenuNodeResponse(
                node.Id,
                node.Code,
                node.Name,
                string.IsNullOrWhiteSpace(tenantMenu?.DisplayNameOverride)
                    ? node.Name
                    : tenantMenu.DisplayNameOverride,
                node.Level,
                node.ModuleCode,
                node.Route,
                string.IsNullOrWhiteSpace(tenantMenu?.IconOverride) ? node.Icon : tenantMenu.IconOverride,
                node.RequiredPermissionCode,
                tenantMenu?.DisplayOrderOverride ?? node.DisplayOrder,
                // No row means "inherit the catalogue default", which is how a node shipped
                // after this Organisation was created still appears for them.
                tenantMenu?.IsEnabled ?? node.IsEnabledByDefault,
                node.IsMandatory,
                tenantMenu?.DisplayNameOverride,
                tenantMenu?.IconOverride,
                tenantMenu?.DisplayOrderOverride,
                children));
        }

        return [.. result.OrderBy(node => node.ResolvedOrder).ThenBy(node => node.CatalogueName, StringComparer.Ordinal)];
    }

    private static List<RoleMenuNodeResponse> BuildMappingTree(
        IReadOnlyList<Domain.Entities.MenuDefinition> nodes,
        IReadOnlyDictionary<Guid, Domain.Entities.RoleMenu> mappingsById,
        IReadOnlySet<string>? heldPermissions,
        Guid? parentId)
    {
        var result = new List<RoleMenuNodeResponse>();

        foreach (var node in nodes.Where(item => item.ParentMenuId == parentId))
        {
            mappingsById.TryGetValue(node.Id, out var mapping);

            var permitted = heldPermissions is null
                            || string.IsNullOrWhiteSpace(node.RequiredPermissionCode)
                            || heldPermissions.Contains(node.RequiredPermissionCode);

            result.Add(new RoleMenuNodeResponse(
                node.Id,
                node.Code,
                node.Name,
                node.Level,
                node.ModuleCode,
                node.Route,
                node.RequiredPermissionCode,
                // No mapping row means visible: requiring an explicit row per role per node
                // would make a newly shipped screen invisible to everybody until somebody
                // remembered to map it.
                mapping?.IsVisible ?? true,
                permitted,
                mapping?.IsLandingPage ?? false,
                BuildMappingTree(nodes, mappingsById, heldPermissions, node.Id)));
        }

        return [.. result.OrderBy(node => node.Name, StringComparer.Ordinal)];
    }
}
