namespace YDots.CAM.Application.Common.Models;

/// <summary>
/// The narrowing a read service applies WITHIN one Organisation.
///
/// IT IS NOT THE ORGANISATION BOUNDARY, and confusing the two would be a serious mistake. The
/// boundary is enforced underneath by the DbContext query filter and cannot be widened from
/// here; this decides how much of the caller own Organisation they see - their own campaigns,
/// their team, or all of it.
/// </summary>
public sealed record AccessScope(Guid TenantId, Guid UserId, IReadOnlyList<string> DataScopes)
{
    /// <summary>The scope of a request with no authenticated user. Sees nothing.</summary>
    public static readonly AccessScope Empty = new(Guid.Empty, Guid.Empty, []);

    /// <summary>
    /// True when the caller is limited to records they own.
    ///
    /// Driven by a data_scope claim rather than by a role, so an Organisation can grant the
    /// same role a different reach without a new role.
    /// </summary>
    public bool IsOwnRecordsOnly =>
        DataScopes.Contains("own", StringComparer.OrdinalIgnoreCase);
}
