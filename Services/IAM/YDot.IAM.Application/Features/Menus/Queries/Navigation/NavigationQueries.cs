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

/// <summary>
/// The catalogue as the AUTHORING screen needs it.
///
/// <see cref="GetMenuCatalogueQuery"/> answers with <c>MenuNode</c>, which is what the sidebar
/// renders: a label, a route, an icon. That is deliberately not enough to edit with - it carries
/// no version to save against, no status, no description, no parent id and none of the
/// mandatory / platform-only / enabled-by-default flags. Editing through it would mean guessing
/// at half the record, so authoring gets its own read.
/// </summary>
public sealed record GetMenuDefinitionsQuery(bool IncludeRetired = false);

/// <summary>
/// The permission codes a catalogue node may be gated on, for the authoring picker.
///
/// WHY THIS IS NOT <c>GET /permissions</c>. That endpoint is gated on
/// <c>iam.permissions.view</c>, which is a TENANT permission - and a SuperAdmin standing at
/// platform level holds no tenant permissions at all, because there is no Organisation whose
/// permissions they would be viewing. So the one person who authors the catalogue was the one
/// person the picker could not load for, and it silently rendered empty. Same data, gated on
/// the permission the authoring screen actually requires.
/// </summary>
public sealed record GetMenuPermissionCodesQuery;

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
    IPermissionRepository permissions,
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

    /// <summary>
    /// Every permission code, for the catalogue authoring picker.
    ///
    /// Codes and names only. The permission catalogue is global rather than Organisation-owned,
    /// so there is nothing here that belongs to one customer.
    /// </summary>
    public async Task<Result<IReadOnlyList<MenuPermissionOptionResponse>>> HandleAsync(
        GetMenuPermissionCodesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await permissions.GetAllAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MenuPermissionOptionResponse>>(
        [
            .. all
                .OrderBy(permission => permission.Code, StringComparer.Ordinal)
                .Select(permission => new MenuPermissionOptionResponse(
                    permission.Code, permission.Name, permission.ModuleCode))
        ]);
    }

    /// <summary>
    /// Every catalogue node, in full, as a tree.
    ///
    /// Gated by the platform manage permission on the endpoint. Retired nodes are excluded by
    /// default and available on request, because the only reason to look at one is to bring it
    /// back.
    /// </summary>
    public async Task<Result<IReadOnlyList<MenuDefinitionResponse>>> HandleAsync(
        GetMenuDefinitionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);

        var nodes = query.IncludeRetired
            ? catalogue
            : catalogue.Where(node => node.Status != MenuStatus.Retired).ToList();

        var namesById = catalogue.ToDictionary(node => node.Id, node => node.Name);

        return Result.Success<IReadOnlyList<MenuDefinitionResponse>>(
            BuildDefinitionTree(nodes, namesById, parentId: null));
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
    ///
    /// <c>IsEnabledForOrganisation</c> is the other half of that honesty, and it was missing.
    /// A node this Organisation has switched off on the OTHER tab of the same screen is
    /// removed from everybody's navigation before role mapping is consulted, so mapping it saved
    /// perfectly and still showed nobody anything — which, from the administrator seat, is
    /// indistinguishable from the save having been thrown away. The flag travels with the node
    /// so the screen can say which of the two levers is the one to pull.
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

        // A PLATFORM ROLE BELONGS TO NO ORGANISATION, so no Organisation may shape its
        // navigation. The role query filter deliberately lets a null-Tenant role through —
        // that is what keeps SuperAdmin loadable from inside an Organisation — which meant
        // this endpoint answered for SUPER_ADMIN to any TenantAdmin who typed its id. The
        // lookup feeding the dropdown already excludes platform roles; this is the same rule,
        // applied where it is actually enforceable.
        if (role.IsPlatformRole)
        {
            return Result.Failure<RoleMenuMappingResponse>(Error.Forbidden(
                "A platform role's navigation is not an organisation's to configure."));
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var roleMenus = await menus.GetRoleMenusAsync(role.Id, cancellationToken);
        var mappingsById = roleMenus.ToDictionary(mapping => mapping.MenuDefinitionId);

        // What the Organisation that owns this role has switched on. Read from the role's own
        // Organisation rather than the caller's, so the answer matches what the people holding
        // the role will actually be shown.
        var tenantMenus = role.TenantId.HasValue
            ? await menus.GetTenantMenusAsync(role.TenantId.Value, cancellationToken)
            : [];

        var overridesById = tenantMenus.ToDictionary(item => item.MenuDefinitionId);

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
            BuildMappingTree(configurable, mappingsById, overridesById, held, parentId: null)));
    }

    private static List<MenuDefinitionResponse> BuildDefinitionTree(
        IReadOnlyList<Domain.Entities.MenuDefinition> nodes,
        IReadOnlyDictionary<Guid, string> namesById,
        Guid? parentId)
    {
        var result = new List<MenuDefinitionResponse>();

        foreach (var node in nodes.Where(item => item.ParentMenuId == parentId))
        {
            result.Add(new MenuDefinitionResponse(
                node.Id,
                node.Code,
                node.Name,
                node.Description,
                node.ParentMenuId,
                node.ParentMenuId is { } id && namesById.TryGetValue(id, out var name) ? name : null,
                node.Level,
                node.ModuleCode,
                node.Route,
                node.Icon,
                node.RequiredPermissionCode,
                node.DisplayOrder,
                node.Status,
                node.IsPlatformOnly,
                node.IsEnabledByDefault,
                node.IsMandatory,
                node.OpensInNewTab,
                node.BadgeKey,
                node.Version,
                BuildDefinitionTree(nodes, namesById, node.Id)));
        }

        return [.. result.OrderBy(node => node.DisplayOrder).ThenBy(node => node.Name, StringComparer.Ordinal)];
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
        IReadOnlyDictionary<Guid, Domain.Entities.TenantMenu> overridesById,
        IReadOnlySet<string>? heldPermissions,
        Guid? parentId)
    {
        var result = new List<(Domain.Entities.MenuDefinition Definition, RoleMenuNodeResponse Node)>();

        foreach (var node in nodes.Where(item => item.ParentMenuId == parentId))
        {
            mappingsById.TryGetValue(node.Id, out var mapping);

            var permitted = heldPermissions is null
                            || string.IsNullOrWhiteSpace(node.RequiredPermissionCode)
                            || heldPermissions.Contains(node.RequiredPermissionCode);

            // The same rule the navigation build applies: no row means "inherit the catalogue
            // default", and a mandatory node is on whatever the row says.
            var enabledForOrganisation = node.IsMandatory
                                         || (overridesById.TryGetValue(node.Id, out var tenantMenu)
                                             ? tenantMenu.IsVisible
                                             : node.IsEnabledByDefault);

            result.Add((node, new RoleMenuNodeResponse(
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
                enabledForOrganisation,
                BuildMappingTree(nodes, mappingsById, overridesById, heldPermissions, node.Id))));
        }

        // THE SAME ORDER AS THE OTHER TAB, AND AS THE SIDEBAR. This sorted by name alone, so
        // the two tabs of one screen listed one menu in two different orders and neither
        // matched the navigation they describe. Display order first, name only to break a tie.
        return
        [
            .. result
                .OrderBy(entry => overridesById.TryGetValue(entry.Definition.Id, out var item)
                    ? item.ResolvedOrder
                    : entry.Definition.DisplayOrder)
                .ThenBy(entry => entry.Node.Name, StringComparer.Ordinal)
                .Select(entry => entry.Node)
        ];
    }
}
