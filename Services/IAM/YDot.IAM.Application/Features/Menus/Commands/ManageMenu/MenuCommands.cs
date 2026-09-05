using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Menus.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Menus.Commands.ManageMenu;

/// <summary>Adds a node to the global catalogue. SuperAdmin only.</summary>
public sealed record CreateMenuDefinitionCommand(CreateMenuDefinitionRequest Request);

/// <summary>Edits a catalogue node. SuperAdmin only.</summary>
public sealed record UpdateMenuDefinitionCommand(Guid MenuId, UpdateMenuDefinitionRequest Request);

/// <summary>An Organisation switching nodes on or off and renaming them.</summary>
public sealed record ConfigureTenantMenuCommand(ConfigureTenantMenuRequest Request);

/// <summary>Mapping menu nodes to a role.</summary>
public sealed record MapRoleMenusCommand(Guid RoleId, MapRoleMenusRequest Request);

/// <summary>Removing a node from the global catalogue. SuperAdmin only.</summary>
public sealed record DeleteMenuDefinitionCommand(Guid MenuId, long ExpectedVersion);

/// <summary>
/// Menu configuration, across all three tables.
///
/// WHO MAY TOUCH WHAT, and why the split matters:
///
/// <code>
/// MenuDefinition  SuperAdmin only   what screens exist in this build
/// TenantMenu      TenantAdmin       which of them this Organisation offers, and what it calls them
/// RoleMenu        TenantAdmin       which roles inside it see each one
/// </code>
///
/// An Organisation editing the CATALOGUE would change what every other Organisation sees, so
/// that endpoint is platform-only. An Organisation editing its own two tables affects nobody
/// else, which is exactly the separation the three-table shape buys.
/// </summary>
public sealed class MenuCommandHandler(
    IMenuRepository menus,
    IRoleRepository roles,
    ITenantRepository tenants,
    IPermissionRepository permissions,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<MenuDefinitionResponse>> HandleAsync(
        CreateMenuDefinitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var code = CodeValue.TryParse(request.Code)?.Value;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<MenuDefinitionResponse>(
                Error.Validation("That menu code is not valid.",
                    [new ValidationError(nameof(request.Code),
                        "Use upper-case letters, digits, underscores or hyphens.")]));
        }

        if (await menus.DefinitionCodeExistsAsync(code, null, cancellationToken))
        {
            return Result.Failure<MenuDefinitionResponse>(
                Error.Duplicate($"A menu node with code {code} already exists."));
        }

        // The tree is three deep by design. Deeper would work structurally but the theme has
        // no design for a fourth level, so it is refused here rather than rendered badly.
        if (request.ParentMenuId.HasValue)
        {
            var parent = await menus.GetDefinitionAsync(request.ParentMenuId.Value, cancellationToken);
            if (parent is null)
            {
                return Result.Failure<MenuDefinitionResponse>(
                    Error.NotFound("That parent menu was not found."));
            }

            if (parent.Level == MenuLevel.ChildSubMenu)
            {
                return Result.Failure<MenuDefinitionResponse>(Error.Validation(
                    "The navigation supports three levels: Menu, Submenu and Child submenu.",
                    [new ValidationError(nameof(request.ParentMenuId),
                        "A child submenu cannot have children of its own.")]));
            }

            if ((int)request.Level != (int)parent.Level + 1)
            {
                return Result.Failure<MenuDefinitionResponse>(Error.Validation(
                    "The level does not follow from the parent.",
                    [new ValidationError(nameof(request.Level),
                        $"A child of a {parent.Level} must be a {(MenuLevel)((int)parent.Level + 1)}.")]));
            }
        }
        else if (request.Level != MenuLevel.Menu)
        {
            return Result.Failure<MenuDefinitionResponse>(Error.Validation(
                "A node with no parent must be a top-level Menu.",
                [new ValidationError(nameof(request.Level), "Choose Menu, or give it a parent.")]));
        }

        // A node guarded by a permission nobody can hold is invisible forever, which looks
        // exactly like a bug. Refused rather than saved.
        if (!string.IsNullOrWhiteSpace(request.RequiredPermissionCode))
        {
            var permission = await permissions.GetByCodeAsync(request.RequiredPermissionCode, cancellationToken);
            if (permission is null)
            {
                return Result.Failure<MenuDefinitionResponse>(Error.Validation(
                    "That permission code was not recognised.",
                    [new ValidationError(nameof(request.RequiredPermissionCode),
                        $"Unknown permission: {request.RequiredPermissionCode}")]));
            }
        }

        var definition = new MenuDefinition
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentMenuId = request.ParentMenuId,
            Level = request.Level,
            ModuleCode = request.ModuleCode.Trim().ToUpperInvariant(),
            Route = request.Route?.Trim(),
            Icon = request.Icon?.Trim(),
            RequiredPermissionCode = request.RequiredPermissionCode?.Trim(),
            DisplayOrder = request.DisplayOrder,
            Status = MenuStatus.Active,
            IsPlatformOnly = request.IsPlatformOnly,
            IsEnabledByDefault = request.IsEnabledByDefault,
            IsMandatory = request.IsMandatory,
            OpensInNewTab = request.OpensInNewTab,
            BadgeKey = request.BadgeKey
        };

        await menus.AddDefinitionAsync(definition, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.MenuConfigured, nameof(MenuDefinition), definition.Id, definition.Name,
            new { definition.Code, definition.Level, definition.ModuleCode },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new MenuDefinitionResponse(
            definition.Id, definition.Code, definition.Name, definition.Description,
            definition.ParentMenuId, null, definition.Level, definition.ModuleCode,
            definition.Route, definition.Icon, definition.RequiredPermissionCode,
            definition.DisplayOrder, definition.Status, definition.IsPlatformOnly,
            definition.IsEnabledByDefault, definition.IsMandatory, definition.OpensInNewTab,
            definition.BadgeKey, definition.Version, []));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateMenuDefinitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var definition = await menus.GetDefinitionAsync(command.MenuId, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That menu node was not found."));
        }

        if (definition.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            definition.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            definition.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.Route is not null)
        {
            definition.Route = string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim();
        }

        if (request.Icon is not null)
        {
            definition.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        }

        if (request.RequiredPermissionCode is not null)
        {
            var code = string.IsNullOrWhiteSpace(request.RequiredPermissionCode)
                ? null
                : request.RequiredPermissionCode.Trim();

            if (code is not null && await permissions.GetByCodeAsync(code, cancellationToken) is null)
            {
                return Result.Failure<OutcomeResponse>(Error.Validation(
                    "That permission code was not recognised.",
                    [new ValidationError(nameof(request.RequiredPermissionCode), $"Unknown permission: {code}")]));
            }

            definition.RequiredPermissionCode = code;
        }

        if (request.DisplayOrder.HasValue)
        {
            definition.DisplayOrder = request.DisplayOrder.Value;
        }

        if (request.Status.HasValue)
        {
            // A mandatory node cannot be retired, or people lose the screen they land on.
            if (definition.IsMandatory && request.Status.Value is MenuStatus.Retired or MenuStatus.Hidden)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Forbidden("That menu item is required and cannot be hidden or retired."));
            }

            definition.Status = request.Status.Value;
        }

        if (request.IsEnabledByDefault.HasValue)
        {
            definition.IsEnabledByDefault = request.IsEnabledByDefault.Value;
        }

        if (request.OpensInNewTab.HasValue)
        {
            definition.OpensInNewTab = request.OpensInNewTab.Value;
        }

        if (request.BadgeKey is not null)
        {
            definition.BadgeKey = string.IsNullOrWhiteSpace(request.BadgeKey) ? null : request.BadgeKey;
        }

        await audit.WriteAsync(
            AuditActionCodes.MenuConfigured, nameof(MenuDefinition), definition.Id, definition.Name,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            definition.Id, definition.Status.ToString(), definition.Version, "Menu node saved.", []));
    }

    /// <summary>
    /// Removing a node from the catalogue.
    ///
    /// DELETING IS THE NARROW CASE, AND RETIRING IS THE USUAL ONE. A catalogue node is global:
    /// the moment any Organisation has configured it, or any role has been mapped against it,
    /// there are rows pointing at it and removing the row they point at leaves orphans behind.
    /// So a node with dependants is refused, and the caller is told to retire it instead -
    /// which hides it everywhere, is reversible, and keeps the history intact.
    ///
    /// What is left is the case delete is actually for: a node added by mistake, five minutes
    /// ago, that nothing has touched yet.
    ///
    /// THREE GUARDS, in the order somebody would hit them:
    ///   a node with children would orphan a whole branch;
    ///   a mandatory node is one people are guaranteed to be able to reach;
    ///   a node anybody has configured or mapped is in use.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteMenuDefinitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var definition = await menus.GetDefinitionAsync(command.MenuId, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That menu node was not found."));
        }

        if (definition.Version != command.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);

        var children = catalogue.Count(node => node.ParentMenuId == definition.Id);
        if (children > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                $"{definition.Name} has {children} item(s) beneath it.",
                [new ValidationError(nameof(command.MenuId),
                    "Remove or re-parent the items underneath it first.")]));
        }

        if (definition.IsMandatory)
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden(
                $"{definition.Name} is required and cannot be removed."));
        }

        var references = await menus.CountDefinitionReferencesAsync(definition.Id, cancellationToken);
        if (references > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                $"{definition.Name} is in use: {references} organisation setting(s) and role "
                + "mapping(s) refer to it.",
                [new ValidationError(nameof(command.MenuId),
                    "Retire it instead - that hides it everywhere and can be undone.")]));
        }

        menus.RemoveDefinition(definition);

        await audit.WriteAsync(
            AuditActionCodes.MenuConfigured, nameof(MenuDefinition), definition.Id, definition.Name,
            new { Deleted = true, definition.Code, definition.Level, definition.ModuleCode },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            definition.Id, "Deleted", definition.Version,
            $"{definition.Name} was removed from the catalogue.", []));
    }

    /// <summary>
    /// An Organisation switching nodes on and off.
    ///
    /// A mandatory node cannot be switched off — the dashboard and My Security have to remain
    /// reachable, or somebody signs in and has nowhere to go. Nor can the section a mandatory
    /// node sits under, because a heading that disappears takes its children with it.
    ///
    /// THE CASCADE IS APPLIED HERE, not merely described. It was documented on this method and
    /// on the controller as something the server enforced, and it was not: whatever arrived was
    /// stored verbatim. Ticking a child while its parent stayed off therefore saved perfectly,
    /// reported success, and changed nothing anybody could see — the navigation build drops the
    /// parent, and a child with no parent in the tree is simply never rendered. Storing the
    /// value the navigation will actually honour is what makes the screen tell the truth.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ConfigureTenantMenuCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var byId = catalogue.ToDictionary(node => node.Id);

        // ONE QUERY, NOT ONE PER NODE. This read the row for each item inside the loop, which
        // is sixty round trips for a screen that saves sixty toggles in one go — and the
        // cascade below needs the whole picture anyway, not one row at a time.
        var existingRows = await menus.GetTenantMenusAsync(tenantId, cancellationToken);
        var existingById = existingRows.ToDictionary(row => row.MenuDefinitionId);

        var requestedById = new Dictionary<Guid, bool>();

        foreach (var item in command.Request.Items ?? [])
        {
            requestedById[item.MenuDefinitionId] = item.IsEnabled;
        }

        MenuDefinition? ParentOf(MenuDefinition definition) =>
            definition.ParentMenuId is { } parentId && byId.TryGetValue(parentId, out var parent)
                ? parent
                : null;

        // What this request asks for one node, falling back to what is already stored and then
        // to the catalogue default. A partial request must not read as "switch off everything
        // it did not mention".
        bool Asked(MenuDefinition definition) =>
            requestedById.TryGetValue(definition.Id, out var requested)
                ? requested
                : existingById.TryGetValue(definition.Id, out var row)
                    ? row.IsEnabled
                    : definition.IsEnabledByDefault;

        bool EnabledInPractice(MenuDefinition definition)
        {
            for (var node = definition; node is not null; node = ParentOf(node))
            {
                if (!Asked(node))
                {
                    return false;
                }
            }

            return true;
        }

        // A mandatory node cannot be switched off, and neither can anything it sits under.
        var mustStayEnabled = new HashSet<Guid>();

        foreach (var mandatory in catalogue.Where(node => node.IsMandatory))
        {
            for (var node = mandatory; node is not null; node = ParentOf(node))
            {
                mustStayEnabled.Add(node.Id);
            }
        }

        var changed = 0;
        var heldBackByParent = 0;

        foreach (var item in command.Request.Items ?? [])
        {
            if (!byId.TryGetValue(item.MenuDefinitionId, out var definition))
            {
                continue;
            }

            // The platform branch is never an Organisation decision.
            if (definition.IsPlatformOnly)
            {
                continue;
            }

            if (mustStayEnabled.Contains(definition.Id) && !item.IsEnabled)
            {
                return Result.Failure<OutcomeResponse>(Error.Forbidden(definition.IsMandatory
                    ? $"{definition.Name} is required and cannot be switched off."
                    : $"{definition.Name} cannot be switched off: a required item sits under it."));
            }

            var isEnabled = EnabledInPractice(definition);

            if (item.IsEnabled && !isEnabled)
            {
                heldBackByParent++;
            }

            existingById.TryGetValue(definition.Id, out var existing);

            if (existing is null)
            {
                await menus.AddTenantMenuAsync(new TenantMenu
                {
                    TenantId = tenantId,
                    BusinessUnitId = tenantContext.BusinessUnitId,
                    MenuDefinitionId = definition.Id,
                    IsEnabled = isEnabled,
                    DisplayNameOverride = item.DisplayNameOverride?.Trim(),
                    IconOverride = item.IconOverride?.Trim(),
                    DisplayOrderOverride = item.DisplayOrderOverride,
                    Status = MenuStatus.Active,
                    IsSystemGenerated = false
                }, cancellationToken);
            }
            else
            {
                existing.IsEnabled = isEnabled;
                existing.DisplayNameOverride = string.IsNullOrWhiteSpace(item.DisplayNameOverride)
                    ? null
                    : item.DisplayNameOverride.Trim();
                existing.IconOverride = string.IsNullOrWhiteSpace(item.IconOverride)
                    ? null
                    : item.IconOverride.Trim();
                existing.DisplayOrderOverride = item.DisplayOrderOverride;
                existing.IsSystemGenerated = false;
            }

            changed++;
        }

        await audit.WriteAsync(
            AuditActionCodes.MenuConfigured, nameof(Tenant), tenantId, tenant.Name,
            new { NodesConfigured = changed, HeldBackByParent = heldBackByParent },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenantId, tenant.Status.ToString(), tenant.Version,
            heldBackByParent == 0
                ? $"Navigation saved. {changed} item(s) configured."
                : $"Navigation saved. {changed} item(s) configured; {heldBackByParent} stayed off "
                  + "because the section above them is off.",
            []));
    }

    /// <summary>
    /// Mapping menu nodes to a role.
    ///
    /// This can only take a node AWAY from a role. A node whose permission the role does not
    /// hold is not made visible by mapping it — the endpoint behind the screen would still
    /// answer 403, so mapping it would produce a menu item that leads to an error page.
    /// Those are dropped rather than saved.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        MapRoleMenusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var role = await roles.GetWithPermissionsAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That role was not found."));
        }

        // A PLATFORM ROLE BELONGS TO NO ORGANISATION. The role query filter lets a null-Tenant
        // role through so SuperAdmin stays loadable from inside an Organisation, which meant a
        // TenantAdmin who typed SUPER_ADMIN's id could write mapping rows against it under
        // their own Organisation. The dropdown never offered it; nor should the endpoint.
        if (role.IsPlatformRole)
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden(
                "A platform role's navigation is not an organisation's to configure."));
        }

        if (role.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        var catalogue = await menus.GetCatalogueAsync(cancellationToken);
        var byId = catalogue.ToDictionary(node => node.Id);

        // What this role can actually reach, so a pointless mapping is not written.
        var held = role.GrantsAllTenantPermissions
            ? null
            : role.RolePermissions
                .Where(rolePermission => !rolePermission.IsDenied)
                .Select(rolePermission => rolePermission.PermissionCode)
                .ToHashSet(StringComparer.Ordinal);

        var existing = await menus.GetRoleMenusAsync(role.Id, cancellationToken);
        menus.RemoveRoleMenus(existing);

        var tenantId = role.TenantId ?? tenantContext.RequireTenantId();

        var requested = (request.VisibleMenuIds ?? []).Distinct().ToHashSet();

        MenuDefinition? ParentOf(MenuDefinition definition) =>
            definition.ParentMenuId is { } parentId && byId.TryGetValue(parentId, out var parent)
                ? parent
                : null;

        bool IsPermitted(MenuDefinition definition) =>
            held is null
            || string.IsNullOrWhiteSpace(definition.RequiredPermissionCode)
            || held.Contains(definition.RequiredPermissionCode);

        // THE SAME CASCADE THE OTHER TAB NEEDS, for the same reason. A node under a hidden
        // parent is not reachable, so storing it visible is a mapping that shows nobody
        // anything - the tree builder drops the parent and the orphan is never rendered.
        //
        // AN ANCESTOR THE SCREEN NEVER OFFERED IS SKIPPED, not read as hidden. A parent whose
        // permission the role lacks has no checkbox, so its absence from the request is not a
        // decision anybody made; the permission filter removes that branch on its own.
        bool AncestorsVisible(MenuDefinition definition)
        {
            for (var parent = ParentOf(definition); parent is not null; parent = ParentOf(parent))
            {
                if (parent.IsPlatformOnly
                    || parent.Status == MenuStatus.Retired
                    || !IsPermitted(parent))
                {
                    continue;
                }

                if (!requested.Contains(parent.Id))
                {
                    return false;
                }
            }

            return true;
        }

        var mapped = 0;
        var hidden = 0;
        var skipped = 0;
        var heldBackByParent = 0;

        // EVERY NODE THIS SCREEN LETS SOMEBODY DECIDE ABOUT GETS A ROW, ticked or not.
        //
        // THE BUG THIS FIXES. The loop used to walk VisibleMenuIds alone and write one
        // IsVisible = true row per ticked node, so an UNTICKED node ended up with no row at
        // all. Every reader - BuildForCurrentUserAsync, BuildForRoleAsync and the mapping
        // screen itself - reads a missing row as "inherit the default", and the default is
        // visible. Unticking an item therefore did nothing whatsoever: it did not hide the
        // item, and the screen showed it ticked again on the next load, so the administrator
        // could not even tell their decision had been discarded.
        //
        // "NO ROW" HAS TO GO ON MEANING VISIBLE, which is why the answer is to write the
        // false rows rather than to invert the reader. A node shipped after this role was
        // last mapped has no row, and it must appear rather than being invisible until
        // somebody remembers to re-save every role.
        //
        // A NODE THE ROLE CANNOT REACH GETS NO ROW EITHER WAY. The screen renders its
        // checkbox disabled - see IsPermitted in GetRoleMenuMappingQuery - so nobody has
        // expressed a preference about it, and the permission filter already removes it. A
        // false row there would silently outlive a later permission grant.
        foreach (var definition in catalogue)
        {
            if (definition.IsPlatformOnly || definition.Status == MenuStatus.Retired)
            {
                continue;
            }

            var permitted = IsPermitted(definition);

            var ticked = requested.Contains(definition.Id);

            if (ticked && !permitted)
            {
                // Ticked, but the role's permission set would still make the screen answer
                // 403. Mapping it would produce a menu item that leads to an error page.
                skipped++;
                continue;
            }

            if (!ticked && !permitted)
            {
                continue;
            }

            var isVisible = ticked && AncestorsVisible(definition);

            if (ticked && !isVisible)
            {
                heldBackByParent++;
            }

            await menus.AddRoleMenuAsync(new RoleMenu
            {
                TenantId = tenantId,
                BusinessUnitId = role.BusinessUnitId,
                RoleId = role.Id,
                MenuDefinitionId = definition.Id,
                IsVisible = isVisible,
                IsLandingPage = isVisible && request.LandingMenuId == definition.Id,
                MappedAtUtc = now,
                MappedByUserId = currentUser.UserId
            }, cancellationToken);

            if (isVisible)
            {
                mapped++;
            }
            else
            {
                hidden++;
            }
        }

        // A ticked id that names nothing in the catalogue, or names the platform branch, is
        // counted separately so the message still accounts for everything that was sent
        // without blaming the role's permissions for it.
        var unknown = requested.Count(
            menuId => !byId.TryGetValue(menuId, out var definition) || definition.IsPlatformOnly);

        await audit.WriteAsync(
            AuditActionCodes.MenuRoleMapped, nameof(Role), role.Id, role.Name,
            new
            {
                Mapped = mapped,
                Hidden = hidden,
                Skipped = skipped,
                HeldBackByParent = heldBackByParent,
                Unrecognised = unknown
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = $"Navigation mapped. {mapped} item(s) visible to this role, {hidden} hidden.";

        if (skipped > 0)
        {
            message += $" {skipped} skipped because this role lacks the permission.";
        }

        if (heldBackByParent > 0)
        {
            message += $" {heldBackByParent} stayed hidden because the section above them is hidden.";
        }

        if (unknown > 0)
        {
            message += $" {unknown} were not recognised.";
        }

        return Result.Success(new OutcomeResponse(
            role.Id, role.Status.ToString(), role.Version, message, []));
    }
}
