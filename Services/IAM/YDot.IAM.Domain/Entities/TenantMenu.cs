using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One Organisation decision about one node of the global menu catalogue: is it switched
/// on, what do we call it, and where does it sit.
///
/// This is the middle of the three menu tables described on <see cref="MenuDefinition"/>.
/// It exists so an Organisation can shape its own navigation — hide a module it has not
/// bought, rename "Donors" to "Supporters", move Reports above Campaigns — without any of
/// that leaking into another Organisation or into the catalogue itself.
///
/// EVERY OVERRIDE IS NULLABLE ON PURPOSE. A null means "inherit whatever the catalogue
/// says". Only the fields an Organisation actually changed are stored, so a later change to
/// the default label or icon still reaches every Organisation that never overrode it. If
/// the columns were populated with copies at creation time, the platform could never
/// improve a label again.
/// </summary>
public class TenantMenu : TenantEntity
{
    public Guid MenuDefinitionId { get; set; }

    public MenuDefinition? MenuDefinition { get; set; }

    /// <summary>
    /// Whether this Organisation offers the node at all. A row with false is how a node is
    /// switched off; the absence of a row means "fall back to the catalogue default".
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Organisation-specific label. Null inherits <see cref="MenuDefinition.Name"/>.</summary>
    public string? DisplayNameOverride { get; set; }

    /// <summary>Organisation-specific icon. Null inherits the catalogue icon.</summary>
    public string? IconOverride { get; set; }

    /// <summary>Organisation-specific ordering. Null inherits the catalogue order.</summary>
    public int? DisplayOrderOverride { get; set; }

    /// <summary>
    /// Lets an Organisation re-parent a node — for example promoting a SubMenu to a top-level
    /// Menu. Null keeps the catalogue structure.
    /// </summary>
    public Guid? ParentOverrideMenuDefinitionId { get; set; }

    public MenuStatus Status { get; set; } = MenuStatus.Active;

    /// <summary>
    /// True when this row was written by the platform while creating the Organisation, as
    /// opposed to by an administrator. Lets a future migration safely refresh untouched rows.
    /// </summary>
    public bool IsSystemGenerated { get; set; }

    public string? Notes { get; set; }

    /// <summary>The label to render, honouring the override when there is one.</summary>
    public string ResolvedName => string.IsNullOrWhiteSpace(DisplayNameOverride)
        ? MenuDefinition?.Name ?? string.Empty
        : DisplayNameOverride;

    /// <summary>The icon to render, honouring the override when there is one.</summary>
    public string? ResolvedIcon => string.IsNullOrWhiteSpace(IconOverride)
        ? MenuDefinition?.Icon
        : IconOverride;

    /// <summary>The sort position to use, honouring the override when there is one.</summary>
    public int ResolvedOrder => DisplayOrderOverride ?? MenuDefinition?.DisplayOrder ?? 0;

    /// <summary>True when this Organisation should actually render the node.</summary>
    public bool IsVisible => IsEnabled && Status == MenuStatus.Active;
}
