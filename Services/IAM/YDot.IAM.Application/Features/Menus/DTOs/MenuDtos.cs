using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Menus.DTOs;

/// <summary>Adding a node to the global catalogue. SuperAdmin only.</summary>
public sealed record CreateMenuDefinitionRequest(
    string Code,
    string Name,
    MenuLevel Level,
    string ModuleCode,
    Guid? ParentMenuId = null,
    string? Route = null,
    string? Icon = null,
    string? RequiredPermissionCode = null,
    string? Description = null,
    int DisplayOrder = 0,
    bool IsPlatformOnly = false,
    bool IsEnabledByDefault = true,
    bool IsMandatory = false,
    bool OpensInNewTab = false,
    string? BadgeKey = null);

/// <summary>Editing a catalogue node.</summary>
public sealed record UpdateMenuDefinitionRequest(
    long ExpectedVersion,
    string? Name = null,
    string? Description = null,
    string? Route = null,
    string? Icon = null,
    string? RequiredPermissionCode = null,
    int? DisplayOrder = null,
    MenuStatus? Status = null,
    bool? IsEnabledByDefault = null,
    bool? OpensInNewTab = null,
    string? BadgeKey = null);

/// <summary>
/// An Organisation switching nodes on or off and renaming them.
///
/// The whole set is sent, so the outcome does not depend on what the screen was showing.
/// </summary>
public sealed record ConfigureTenantMenuRequest(IReadOnlyList<TenantMenuItemRequest> Items);

/// <summary>One Organisation menu decision.</summary>
public sealed record TenantMenuItemRequest(
    Guid MenuDefinitionId,
    bool IsEnabled,
    string? DisplayNameOverride = null,
    string? IconOverride = null,
    int? DisplayOrderOverride = null);

/// <summary>
/// Mapping menu nodes to a role.
///
/// This can only ever TAKE a node away from a role, never grant access the permission set
/// does not already allow. Permission decides what is permitted; this decides what is
/// offered.
/// </summary>
public sealed record MapRoleMenusRequest(
    IReadOnlyList<Guid> VisibleMenuIds,
    long ExpectedVersion,
    Guid? LandingMenuId = null);

/// <summary>One node of the catalogue, as the configuration screens show it.</summary>
public sealed record MenuDefinitionResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentMenuId,
    string? ParentName,
    MenuLevel Level,
    string ModuleCode,
    string? Route,
    string? Icon,
    string? RequiredPermissionCode,
    int DisplayOrder,
    MenuStatus Status,
    bool IsPlatformOnly,
    bool IsEnabledByDefault,
    bool IsMandatory,
    bool OpensInNewTab,
    string? BadgeKey,
    long Version,
    IReadOnlyList<MenuDefinitionResponse> Children);

/// <summary>
/// The Organisation menu configuration screen: every catalogue node with this Organisation
/// decision beside it, so an administrator sees what they have and what they could have.
/// </summary>
public sealed record TenantMenuConfigurationResponse(
    Guid TenantId,
    string TenantName,
    IReadOnlyList<TenantMenuNodeResponse> Nodes);

/// <summary>One node with its Organisation overrides resolved.</summary>
public sealed record TenantMenuNodeResponse(
    Guid MenuDefinitionId,
    string Code,
    string CatalogueName,
    string ResolvedName,
    MenuLevel Level,
    string ModuleCode,
    string? Route,
    string? ResolvedIcon,
    string? RequiredPermissionCode,
    int ResolvedOrder,
    bool IsEnabled,
    bool IsMandatory,
    string? DisplayNameOverride,
    string? IconOverride,
    int? DisplayOrderOverride,
    IReadOnlyList<TenantMenuNodeResponse> Children);

/// <summary>The menu-mapping screen for one role.</summary>
public sealed record RoleMenuMappingResponse(
    Guid RoleId,
    string RoleName,
    Guid? LandingMenuId,
    IReadOnlyList<RoleMenuNodeResponse> Nodes);

/// <summary>
/// One node in the role mapping.
///
/// <c>IsPermitted</c> says whether the role holds the permission the node needs. When it is
/// false the checkbox is shown disabled, because ticking it would achieve nothing — the
/// endpoint behind the screen would still answer 403.
/// </summary>
public sealed record RoleMenuNodeResponse(
    Guid MenuDefinitionId,
    string Code,
    string Name,
    MenuLevel Level,
    string ModuleCode,
    string? Route,
    string? RequiredPermissionCode,
    bool IsVisible,
    bool IsPermitted,
    bool IsLandingPage,
    IReadOnlyList<RoleMenuNodeResponse> Children);

/// <summary>
/// The navigation the signed-in caller should render, plus where to land.
///
/// This is the response the Angular shell calls once after sign-in and after every
/// Organisation switch.
/// </summary>
public sealed record NavigationResponse(
    IReadOnlyList<Common.Models.MenuNode> Menu,
    string? LandingRoute,
    Guid? TenantId,
    string? TenantName,
    AccessScopeType Scope,
    bool IsTenantMode,
    bool IsSuperAdmin);
