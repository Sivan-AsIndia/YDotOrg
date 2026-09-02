using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Assembles the navigation tree the client renders.
///
/// THE FILTERS, IN THE ORDER IsVisibleToCaller APPLIES THEM. Each one can only remove nodes,
/// never add them, so the result can never offer more than the caller is entitled to:
///
/// <code>
/// 1. catalogue          what screens exist in this build
/// 2. platform-only      dropped unless the caller is SuperAdmin outside an Organisation
/// 3. no Organisation    Tenant work dropped for a SuperAdmin who has not entered one
/// 4. Organisation       dropped if this Tenant switched the node off
/// 5. approval           dropped while the Organisation is still onboarding
/// 6. permission         dropped if the caller lacks RequiredPermissionCode
/// 7. role mapping       dropped if every one of the caller roles hides it
/// 8. empty groups       a group whose children all vanished is dropped too (in BuildTree)
/// </code>
///
/// STEP 8 IS THE ONE PEOPLE FORGET. Without it the sidebar shows a heading that expands to
/// nothing, which reads as a broken page rather than as an absent feature.
///
/// A NODE THE CALLER CANNOT USE IS ABSENT, NOT DISABLED. A greyed-out item still tells
/// somebody the screen exists and invites them to go looking for a way in.
/// </summary>
public sealed class MenuBuilderService(
    IMenuRepository menus,
    IRoleRepository roles,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : IMenuBuilderService
{
    public async Task<IReadOnlyList<MenuNode>> BuildForCurrentUserAsync(CancellationToken cancellationToken)
    {
        var catalogue = await menus.GetCatalogueAsync(cancellationToken);

        // ---- 4. Organisation overrides ---------------------------------------------------
        var tenantMenus = tenantContext.TenantId.HasValue
            ? await menus.GetTenantMenusAsync(tenantContext.TenantId.Value, cancellationToken)
            : [];

        var overridesById = tenantMenus.ToDictionary(item => item.MenuDefinitionId);

        // ---- 7. Role mappings ------------------------------------------------------------------
        var roleIds = await ResolveRoleIdsAsync(cancellationToken);

        var roleMenus = roleIds.Count == 0
            ? []
            : await menus.GetRoleMenusForRolesAsync(roleIds, cancellationToken);

        // A node is hidden by role mapping only when EVERY role that maps it hides it. If any
        // role the person holds says visible, they see it - which is the correct reading of
        // holding several roles.
        var hiddenByEveryRole = roleMenus
            .GroupBy(mapping => mapping.MenuDefinitionId)
            .Where(group => group.All(mapping => !mapping.IsVisible))
            .Select(group => group.Key)
            .ToHashSet();

        var landingMenuId = roleMenus
            .Where(mapping => mapping.IsLandingPage && mapping.IsVisible)
            .Select(mapping => (Guid?)mapping.MenuDefinitionId)
            .FirstOrDefault();

        var visible = catalogue
            .Where(node => IsVisibleToCaller(node, overridesById, hiddenByEveryRole))
            .ToList();

        return BuildTree(visible, overridesById, landingMenuId, parentId: null);
    }

    /// <summary>
    /// The navigation a given role would see, for the mapping screen preview.
    ///
    /// Built from the ROLE permission set rather than the caller, so an administrator can see
    /// what a Volunteer gets without having to be one.
    /// </summary>
    public async Task<IReadOnlyList<MenuNode>> BuildForRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var role = await roles.GetWithPermissionsAsync(roleId, cancellationToken);

        if (role is null)
        {
            return [];
        }

        var held = role.GrantsAllTenantPermissions
            ? null
            : role.RolePermissions
                .Where(grant => !grant.IsDenied)
                .Select(grant => grant.PermissionCode)
                .ToHashSet(StringComparer.Ordinal);

        var tenantMenus = role.TenantId.HasValue
            ? await menus.GetTenantMenusAsync(role.TenantId.Value, cancellationToken)
            : [];

        var overridesById = tenantMenus.ToDictionary(item => item.MenuDefinitionId);

        var roleMenus = await menus.GetRoleMenusAsync(roleId, cancellationToken);

        var hidden = roleMenus
            .Where(mapping => !mapping.IsVisible)
            .Select(mapping => mapping.MenuDefinitionId)
            .ToHashSet();

        var landingMenuId = roleMenus
            .Where(mapping => mapping.IsLandingPage)
            .Select(mapping => (Guid?)mapping.MenuDefinitionId)
            .FirstOrDefault();

        var visible = catalogue
            .Where(node => node.IsVisible)
            // A role belongs to an Organisation, so it never sees the platform branch.
            .Where(node => !node.IsPlatformOnly)
            .Where(node => IsEnabledForTenant(node, overridesById))
            .Where(node => held is null
                           || string.IsNullOrWhiteSpace(node.RequiredPermissionCode)
                           || held.Contains(node.RequiredPermissionCode))
            .Where(node => !hidden.Contains(node.Id))
            .ToList();

        return BuildTree(visible, overridesById, landingMenuId, parentId: null);
    }

    /// <summary>
    /// The whole catalogue as a tree, with NO permission filtering, for the configuration
    /// screens. Reaching it requires the menu-configure permission, which is what makes the
    /// absence of filtering here safe.
    /// </summary>
    public async Task<IReadOnlyList<MenuNode>> BuildCatalogueAsync(
        Guid? tenantId, bool includePlatformNodes, CancellationToken cancellationToken)
    {
        var catalogue = await menus.GetCatalogueAsync(cancellationToken);

        var tenantMenus = tenantId.HasValue
            ? await menus.GetTenantMenusAsync(tenantId.Value, cancellationToken)
            : [];

        var overridesById = tenantMenus.ToDictionary(item => item.MenuDefinitionId);

        var nodes = catalogue
            .Where(node => node.Status != MenuStatus.Retired)
            .Where(node => includePlatformNodes || !node.IsPlatformOnly)
            .ToList();

        return BuildTree(nodes, overridesById, landingMenuId: null, parentId: null);
    }

    /// <summary>
    /// Where the caller should land after signing in.
    ///
    /// A role landing page wins; otherwise the first navigable node in the tree. Falling back
    /// to the tree rather than to a hard-coded route means somebody whose roles exclude the
    /// dashboard still lands somewhere they can actually use.
    /// </summary>
    public async Task<string?> ResolveLandingRouteAsync(CancellationToken cancellationToken) =>
        ResolveLandingRoute(await BuildForCurrentUserAsync(cancellationToken));

    /// <summary>
    /// The same rule, applied to a tree the caller has already built.
    ///
    /// <c>GET /navigation</c> needs the tree AND the landing route, and it used to get them by
    /// building the tree twice. The rule lives here so the two answers cannot drift.
    /// </summary>
    public string? ResolveLandingRoute(IReadOnlyList<MenuNode> menu)
    {
        var landing = FindFirst(menu, node => node.IsLandingPage && !node.IsGroupOnly);
        if (landing is not null)
        {
            return landing.Route;
        }

        return FindFirst(menu, node => !node.IsGroupOnly)?.Route;
    }

    /// <summary>
    /// The modules whose screens work without an Organisation selected.
    ///
    /// CORE is the dashboard and GM is the global master data - countries, currencies, time zones
    /// - which are platform reference data rather than any Organisation's. Everything else needs
    /// to know whose records it is showing.
    /// </summary>
    private static readonly HashSet<string> PlatformWideModules =
        new(StringComparer.Ordinal) { "CORE", "GM", "PLATFORM" };

    /// <summary>
    /// The only nodes an Organisation sees before it is approved.
    ///
    /// THIS IS AN ALLOWLIST, NOT A BLOCKLIST, and the difference matters. A blocklist would name
    /// the things to hide, so a screen added next year would be visible during onboarding until
    /// somebody remembered to add it. This names the few things to KEEP, so anything new is
    /// hidden by default and appears only when a person decides it should. When you are unsure
    /// which way round to write a rule like this, pick the one whose mistakes are boring.
    ///
    /// AN UNAPPROVED ORGANISATION HAS ONE JOB: finish the profile, attach the registration
    /// documents and submit for approval - or read why it came back and correct it. Every
    /// other module was showing in full from the moment the Organisation was created, because
    /// the TenantAdmin role carries GrantsAllTenantPermissions from creation and the default
    /// TenantMenu rows are written then too, so neither the permission filter nor the
    /// Organisation filter removed anything. The result was a sidebar offering Fundraising to
    /// a body nobody has yet agreed to do business with.
    ///
    /// MY SECURITY STAYS, because a person must always be able to change their own password
    /// and manage their second factor, whatever their Organisation status is. Its parent
    /// heading has to be listed too or filter 7 would drop the heading and take the child
    /// with it.
    ///
    /// The codes are the ones declared in <c>MenuCatalogue</c>. A node added there is invisible
    /// during onboarding unless it is named here, which is the safe direction to fail.
    /// </summary>
    private static readonly HashSet<string> OnboardingMenuCodes =
        new(StringComparer.Ordinal)
        {
            "ORGANISATION",       // the heading
            "ORG_PROFILE",        // profile, documents, submit for approval
            "ADMINISTRATION",     // the heading, for the one child below
            "ADMIN_MY_SECURITY",
        };

    /// <summary>Filters 2 through 7 for one node.</summary>
    private bool IsVisibleToCaller(
        MenuDefinition node,
        IReadOnlyDictionary<Guid, TenantMenu> overridesById,
        IReadOnlySet<Guid> hiddenByEveryRole)
    {
        // 1. Retired or hidden in the catalogue.
        if (!node.IsVisible)
        {
            return false;
        }

        // 2. The platform branch. SuperAdmin only, whatever an Organisation configures - AND only
        // while they are not standing inside an Organisation.
        //
        // A SuperAdmin who has entered an Organisation from the switcher is acting AS that
        // Organisation, and platform work does not belong in that context: creating an
        // Organisation from inside another one produces something wholly unrelated to the
        // Organisation named at the top of the screen, which is at best confusing and at worst
        // done by accident. Leaving the Organisation - selecting none - brings the branch back.
        if (node.IsPlatformOnly && (!currentUser.IsSuperAdmin || tenantContext.TenantId.HasValue))
        {
            return false;
        }

        // 3. Tenant work, with no Organisation to do it in.
        //
        // A SuperAdmin who has not entered an Organisation was still offered the whole
        // Organisation branch - Organisation Profile, Departments, Branches and Units, Settings -
        // and every one of those screens reads "my organisation", which for them is nothing. The
        // result was a menu full of items that answered "This organisation could not be loaded".
        //
        // Platform, the dashboard and the global masters are the three things that mean something
        // without an Organisation; everything else appears the moment they enter one.
        if (currentUser.IsSuperAdmin
            && !tenantContext.TenantId.HasValue
            && !node.IsPlatformOnly
            && !PlatformWideModules.Contains(node.ModuleCode))
        {
            return false;
        }

        // 4. Switched off by this Organisation. The platform branch is exempt, because it is
        // not an Organisation decision to make.
        if (!node.IsPlatformOnly && !IsEnabledForTenant(node, overridesById))
        {
            return false;
        }

        // 5. The Organisation has not been approved yet.
        //
        // SUPERADMIN IS EXEMPT, and has to be. Entering an Organisation that is still onboarding
        // is precisely how a submission gets reviewed - see SelectTenantCommand - so filtering
        // their menu down to the profile screen would remove the reason they went in.
        if (!currentUser.IsSuperAdmin
            && tenantContext.TenantStatus.HasValue
            && Tenant.IsOnboardingStatus(tenantContext.TenantStatus.Value)
            && !OnboardingMenuCodes.Contains(node.Code))
        {
            return false;
        }

        // 6. The permission gate. This is the real one: a node whose permission the caller
        // lacks leads to a screen whose endpoints would answer 403.
        if (!string.IsNullOrWhiteSpace(node.RequiredPermissionCode)
            && !currentUser.HasPermission(node.RequiredPermissionCode))
        {
            return false;
        }

        // 7. Hidden for every role the caller holds. Cosmetic, and it can only ever subtract.
        return !hiddenByEveryRole.Contains(node.Id);
    }

    private static bool IsEnabledForTenant(
        MenuDefinition node, IReadOnlyDictionary<Guid, TenantMenu> overridesById)
    {
        // No row means "inherit the catalogue default", which is why a node shipped after an
        // Organisation was created still appears for them.
        if (!overridesById.TryGetValue(node.Id, out var tenantMenu))
        {
            return node.IsEnabledByDefault;
        }

        // A mandatory node cannot be switched off, whatever the row says. Belt and braces
        // against a row written before the flag existed.
        return node.IsMandatory || tenantMenu.IsVisible;
    }

    /// <summary>
    /// Turns the flat list into a tree, applying the Organisation label, icon and order
    /// overrides, and dropping any group left empty by the filtering above.
    /// </summary>
    private static List<MenuNode> BuildTree(
        IReadOnlyList<MenuDefinition> nodes,
        IReadOnlyDictionary<Guid, TenantMenu> overridesById,
        Guid? landingMenuId,
        Guid? parentId)
    {
        var result = new List<MenuNode>();

        foreach (var node in nodes.Where(item => item.ParentMenuId == parentId))
        {
            var children = BuildTree(nodes, overridesById, landingMenuId, node.Id);

            // 8. A group whose children all disappeared is dropped too. Without this the
            // sidebar shows a heading that expands to nothing.
            if (string.IsNullOrWhiteSpace(node.Route) && children.Count == 0)
            {
                continue;
            }

            overridesById.TryGetValue(node.Id, out var tenantMenu);

            result.Add(new MenuNode(
                node.Id,
                node.Code,
                string.IsNullOrWhiteSpace(tenantMenu?.DisplayNameOverride)
                    ? node.Name
                    : tenantMenu.DisplayNameOverride,
                node.Level,
                node.ModuleCode,
                node.Route,
                string.IsNullOrWhiteSpace(tenantMenu?.IconOverride) ? node.Icon : tenantMenu.IconOverride,
                node.RequiredPermissionCode,
                tenantMenu?.DisplayOrderOverride ?? node.DisplayOrder,
                node.OpensInNewTab,
                node.BadgeKey,
                landingMenuId == node.Id,
                children));
        }

        return [.. result.OrderBy(node => node.DisplayOrder).ThenBy(node => node.Name, StringComparer.Ordinal)];
    }

    /// <summary>Depth-first search for the first node matching a predicate.</summary>
    private static MenuNode? FindFirst(IReadOnlyList<MenuNode> nodes, Func<MenuNode, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
            {
                return node;
            }

            var match = FindFirst(node.Children, predicate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// The roles the caller holds, for the mapping filter.
    ///
    /// Resolved from the role CODES in the token rather than a database read, because this
    /// runs on every navigation build. SuperAdmin skips it entirely: the platform branch is
    /// gated on the flag, not on a mapping.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveRoleIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Roles.Count == 0 || !tenantContext.TenantId.HasValue)
        {
            return [];
        }

        var assignable = await roles.GetAssignableAsync(tenantContext.TenantId.Value, cancellationToken);

        return
        [
            .. assignable
                .Where(role => currentUser.Roles.Contains(role.Code, StringComparer.Ordinal))
                .Select(role => role.Id)
        ];
    }
}
