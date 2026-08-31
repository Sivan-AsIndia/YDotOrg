using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One node of the navigation tree: a Menu, a SubMenu or a ChildSubMenu.
///
/// THE THREE-TABLE SHAPE, AND WHY. The brief asks for a dynamically generated menu with
/// three levels. That is modelled as three tables rather than one:
///
/// <code>
/// MenuDefinition  (global)        what screens exist at all
///     |
///     +-- TenantMenu  (per Tenant)  which of them this Organisation switched on,
///     |                             and what it calls them
///     |
///     +-- RoleMenu    (per Tenant)  which roles inside it can see each one
/// </code>
///
/// This table is the first of the three and is deliberately GLOBAL — no TenantId. A screen
/// either exists in the deployed software or it does not; that is a fact about the build,
/// not about a customer. Copying it per Organisation would mean every new screen needed a
/// row inserted into every Tenant, and a Tenant created next year would silently miss
/// anything shipped before it.
///
/// What varies per Organisation is captured next door in <see cref="TenantMenu"/>, so an
/// Organisation can rename, reorder, hide or never enable a node without the catalogue
/// itself being touched.
///
/// THE TREE IS SELF-REFERENCING. <see cref="ParentMenuId"/> gives arbitrary depth;
/// <see cref="Level"/> records which of the three tiers the node is meant to occupy, so the
/// UI can style each tier and a validator can refuse a fourth level rather than rendering
/// something the theme has no design for.
///
/// VISIBILITY IS PERMISSION-DRIVEN. <see cref="RequiredPermissionCode"/> is what makes the
/// menu honest: a node whose permission the caller does not hold is not returned at all, so
/// the navigation never offers a screen that would answer 403.
/// </summary>
public class MenuDefinition : AuditEntity, ICodedEntity
{
    /// <summary>Globally unique, for example IAM_USERS or IAM_USERS_DIRECTORY.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The default label. An Organisation may override it in <see cref="TenantMenu"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Null for a top-level Menu.</summary>
    public Guid? ParentMenuId { get; set; }

    public MenuDefinition? Parent { get; set; }

    public ICollection<MenuDefinition> Children { get; set; } = [];

    public MenuLevel Level { get; set; } = MenuLevel.Menu;

    /// <summary>The section that owns it: IAM, DON, CAM, GM.</summary>
    public string ModuleCode { get; set; } = string.Empty;

    /// <summary>
    /// The Angular route this node opens, for example <c>/app/administration/users</c>.
    /// Null for a node that only groups its children and navigates nowhere itself.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>Icon token the sidebar renders. Matches the existing theme icon set.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// The permission a caller must hold for this node to appear. Null means it is visible
    /// to any authenticated user of the Organisation.
    /// </summary>
    public string? RequiredPermissionCode { get; set; }

    /// <summary>Ordering among siblings. Lower first.</summary>
    public int DisplayOrder { get; set; }

    public MenuStatus Status { get; set; } = MenuStatus.Active;

    /// <summary>
    /// True for nodes only SuperAdmin ever sees — Organisation management, BusinessUnit
    /// settings, the approval queue. These never appear for a Tenant user however their
    /// roles are configured.
    /// </summary>
    public bool IsPlatformOnly { get; set; }

    /// <summary>
    /// True when a new Organisation gets this node switched on automatically. The core IAM
    /// screens are; an optional module is not.
    /// </summary>
    public bool IsEnabledByDefault { get; set; } = true;

    /// <summary>
    /// True when an Organisation may not switch it off. The dashboard and the user profile
    /// have to remain reachable or the person has nowhere to land after signing in.
    /// </summary>
    public bool IsMandatory { get; set; }

    /// <summary>Opens in a new tab rather than routing inside the shell.</summary>
    public bool OpensInNewTab { get; set; }

    /// <summary>Optional badge token, for example a pending-count key the UI resolves.</summary>
    public string? BadgeKey { get; set; }

    public ICollection<TenantMenu> TenantMenus { get; set; } = [];

    public ICollection<RoleMenu> RoleMenus { get; set; } = [];

    /// <summary>True when the node is a container: it has children and no route of its own.</summary>
    public bool IsGroupOnly => string.IsNullOrWhiteSpace(Route);

    public bool IsVisible => Status == MenuStatus.Active;
}
