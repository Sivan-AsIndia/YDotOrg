namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// The difference a proposed change would make, so an administrator can see the consequence
/// before committing rather than after.
///
/// Adding one role to somebody who already holds three is not obviously safe: the new role
/// may overlap entirely, or may quietly hand over an export permission nobody intended.
/// Showing gained and lost side by side turns that from a guess into a decision.
/// </summary>
public sealed record AccessComparison(
    IReadOnlyList<string> Gained,
    IReadOnlyList<string> Lost,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> SensitiveGained)
{
    public bool HasChanges => Gained.Count > 0 || Lost.Count > 0;

    /// <summary>True when the change hands over something marked sensitive, which needs a reason.</summary>
    public bool RequiresJustification => SensitiveGained.Count > 0;

    public static AccessComparison Between(
        IReadOnlySet<string> before,
        IReadOnlySet<string> after,
        Func<string, bool> isSensitive)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(isSensitive);

        var gained = after.Except(before, StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
        var lost = before.Except(after, StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
        var unchanged = before.Intersect(after, StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();

        return new AccessComparison(gained, lost, unchanged, [.. gained.Where(isSensitive)]);
    }
}
