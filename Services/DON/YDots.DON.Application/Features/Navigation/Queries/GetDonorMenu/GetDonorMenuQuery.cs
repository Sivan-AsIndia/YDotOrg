using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;

namespace YDots.DON.Application.Features.Navigation.Queries.GetDonorMenu;

/// <summary>GET /api/v1/donors/menu. The role-aware menu for the Relationships group.</summary>
public sealed record GetDonorMenuQuery;

/// <summary>One menu item the caller is allowed to see.</summary>
public sealed record MenuItemResponse(string ScreenId, string Label, string Route, string ViewPermission);

/// <summary>
/// What the Angular sidebar renders for this section, plus the flags that decide whether a
/// sensitive field or a controlled export is offered at all.
/// </summary>
public sealed record DonorMenuResponse(
    string MenuGroup,
    IReadOnlyList<MenuItemResponse> Items,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> VisibleSensitiveFields,
    bool CanSeeSensitiveContact,
    bool CanSeeConfidentialEvidence,
    bool CanExport);

/// <summary>
/// Builds the role-based menu.
///
/// This is the extension point for role-driven navigation: the menu is derived from the
/// permission claims already inside the access token, so adding a screen means adding one row
/// to <see cref="MenuCatalogue"/> and one permission to the role in IAM. Nothing else changes,
/// and no menu table is needed.
///
/// The rule from UI section 2 still holds: hiding a menu entry is a convenience, never the
/// authorisation. Each route is rechecked by [HasPermission] when it is actually called.
/// </summary>
public sealed class GetDonorMenuQueryHandler(ICurrentUser currentUser)
{
    public Task<Result<DonorMenuResponse>> HandleAsync(
        GetDonorMenuQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        _ = cancellationToken;

        var permissions = currentUser.Permissions;

        var items = MenuCatalogue.VisibleFor(permissions)
            .Select(entry => new MenuItemResponse(entry.ScreenId, entry.Label, entry.Route, entry.ViewPermission))
            .ToList();

        var visibleSensitiveFields = MenuCatalogue.SensitiveFields
            .Where(pair => permissions.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        var response = new DonorMenuResponse(
            MenuCatalogue.MenuGroup,
            items,
            currentUser.Roles,
            [.. permissions.Where(code => code.StartsWith("don.", StringComparison.Ordinal)
                                          || string.Equals(code, PermissionCodes.DonView, StringComparison.Ordinal)).Order(StringComparer.Ordinal)],
            visibleSensitiveFields,
            currentUser.HasPermission(PermissionCodes.DonorsViewSensitiveContact),
            currentUser.HasPermission(PermissionCodes.DonorsViewConfidentialEvidence),
            currentUser.HasPermission(PermissionCodes.DonorsExport));

        return Task.FromResult(Result.Success(response));
    }
}
