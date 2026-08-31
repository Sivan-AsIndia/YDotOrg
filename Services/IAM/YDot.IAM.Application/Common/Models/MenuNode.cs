using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// One node of the navigation tree as the client receives it, with its children nested
/// inside it.
///
/// THE TREE IS ASSEMBLED ON THE SERVER, DELIBERATELY. The three tables behind it —
/// the global catalogue, the Organisation overrides and the role mappings — plus the
/// caller permission set have to be combined before anything can be drawn. Sending the
/// raw tables and letting Angular join them would put an authorisation decision in the
/// browser, where it can be edited. Sending the finished tree means the client renders
/// what it is given and nothing more.
///
/// A node the caller may not see is not returned at all, rather than returned with a
/// disabled flag. A greyed-out menu item still tells somebody the screen exists.
/// </summary>
public sealed record MenuNode(
    Guid Id,
    string Code,
    string Name,
    MenuLevel Level,
    string ModuleCode,
    string? Route,
    string? Icon,
    string? RequiredPermissionCode,
    int DisplayOrder,
    bool OpensInNewTab,
    string? BadgeKey,
    bool IsLandingPage,
    IReadOnlyList<MenuNode> Children)
{
    /// <summary>True when the node only groups its children and navigates nowhere itself.</summary>
    public bool IsGroupOnly => string.IsNullOrWhiteSpace(Route);

    public bool HasChildren => Children.Count > 0;
}
