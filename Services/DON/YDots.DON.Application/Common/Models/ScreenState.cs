namespace YDots.DON.Application.Common.Models;

/// <summary>
/// The eight screen states every Donors view must be able to present (UI section 4.x.4).
/// Returned inside the view payloads so the UI does not have to guess which one applies.
/// </summary>
public static class ScreenState
{
    public const string Initial = "Initial";
    public const string Loading = "Loading";
    public const string Empty = "Empty";
    public const string Validation = "Validation";
    public const string Duplicate = "Duplicate";
    public const string NoAccess = "NoAccess";
    public const string Conflict = "Conflict";
    public const string DependencyFailure = "DependencyFailure";
    public const string Success = "Success";
}
