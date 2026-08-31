using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// Which roles inside an Organisation can see which menu node. The third of the three menu
/// tables described on <see cref="MenuDefinition"/>, and the one the "menu mapping" screen
/// in the brief edits.
///
/// HOW THIS RELATES TO PERMISSIONS. A node already has
/// <see cref="MenuDefinition.RequiredPermissionCode"/>, and that is the real gate: if the
/// caller lacks the permission, the endpoint behind the screen answers 403 whatever the
/// navigation says. This table is the second, narrower filter — it lets an Organisation
/// decide that although its Finance role technically holds <c>don.donors.view</c>, the
/// Donors menu should not clutter their sidebar.
///
/// So: permission decides what is permitted, RoleMenu decides what is offered. A row here
/// can only ever take a node away from a role, never grant access the permission set does
/// not already allow. That ordering matters, because the opposite would turn a cosmetic
/// navigation screen into an authorisation bypass.
///
/// NO ROWS MEANS NO RESTRICTION. If nothing maps a node, every role that holds the
/// permission sees it. Requiring an explicit row per role per node would mean a new screen
/// was invisible to everybody until somebody remembered to map it.
/// </summary>
public class RoleMenu : TenantEntity
{
    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public Guid MenuDefinitionId { get; set; }

    public MenuDefinition? MenuDefinition { get; set; }

    /// <summary>
    /// False hides the node from this role. The row is kept rather than deleted so the
    /// mapping screen can show an explicit "no" and an administrator can see the decision
    /// was made deliberately.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Lets a role land somewhere other than the default after signing in. At most one row
    /// per role carries this.
    /// </summary>
    public bool IsLandingPage { get; set; }

    /// <summary>Role-specific ordering. Null inherits the Organisation or catalogue order.</summary>
    public int? DisplayOrderOverride { get; set; }

    public DateTimeOffset MappedAtUtc { get; set; }

    public Guid MappedByUserId { get; set; }

    public string? Notes { get; set; }
}
