namespace YDot.PAY.Application.Common.Models;

/// <summary>
/// The narrowing a read service applies WITHIN one Organisation.
///
/// IT IS NOT THE ORGANISATION BOUNDARY. That is enforced underneath by the DbContext query
/// filter and cannot be widened from here.
/// </summary>
public sealed record AccessScope(Guid TenantId, Guid UserId, IReadOnlyList<string> DataScopes)
{
    public static readonly AccessScope Empty = new(Guid.Empty, Guid.Empty, []);

    public bool IsOwnRecordsOnly =>
        DataScopes.Contains("own", StringComparer.OrdinalIgnoreCase);
}
