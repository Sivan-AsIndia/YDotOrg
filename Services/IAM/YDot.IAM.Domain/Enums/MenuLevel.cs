namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Depth of a node in the navigation tree. The brief asks for Menu, Submenu and
/// ChildSubMenu, generated dynamically.
///
/// The tree is stored as a self-referencing <c>ParentMenuId</c>, so the structure would
/// work at any depth; this enum records the intended level so the UI can style each tier
/// and so a validator can refuse a fourth level rather than silently rendering something
/// the theme has no design for.
/// </summary>
public enum MenuLevel
{
    /// <summary>Top-level item in the sidebar.</summary>
    Menu = 0,

    /// <summary>Second level, revealed by expanding a Menu.</summary>
    SubMenu = 1,

    /// <summary>Third level, revealed by expanding a SubMenu.</summary>
    ChildSubMenu = 2
}
