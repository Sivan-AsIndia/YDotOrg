using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// The <c>Code</c> that every master table carries.
///
/// The brief requires a unique Code column on all master tables even where the
/// specification does not mention one, and this type is what keeps those values
/// comparable. Codes are upper-cased, trimmed, and restricted to letters, digits,
/// underscore and hyphen, so "campaign manager", "Campaign_Manager" and "CAMPAIGN_MANAGER"
/// cannot all be inserted as three different rows that a human would read as one.
///
/// Spaces are folded to underscores rather than rejected, because a code is very often
/// derived from a name typed by a person and failing the whole save over a space is a poor
/// trade for the little it protects.
/// </summary>
public sealed partial record CodeValue
{
    public const int MaximumLength = 50;

    private CodeValue(string value) => Value = value;

    public string Value { get; }

    public static CodeValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToUpperInvariant().Replace(' ', '_');

        return normalised.Length <= MaximumLength && CodePattern().IsMatch(normalised)
            ? new CodeValue(normalised)
            : null;
    }

    public static CodeValue Parse(string candidate) =>
        TryParse(candidate) ?? throw new ArgumentException($"'{candidate}' is not a valid code.", nameof(candidate));

    /// <summary>
    /// Best-effort code from a display name: "Campaign Manager" becomes "CAMPAIGN_MANAGER".
    /// Characters that are not legal in a code are dropped, and the result is truncated to
    /// the column length. Used when seeding and when a screen offers to fill the code in.
    /// </summary>
    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var cleaned = new string([.. name.Trim().ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')]);

        // Collapse runs of underscores so "A  -  B" does not become "A______B".
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('_');

        return cleaned.Length <= MaximumLength ? cleaned : cleaned[..MaximumLength].TrimEnd('_');
    }

    public override string ToString() => Value;

    public static implicit operator string(CodeValue code) => code.Value;

    [GeneratedRegex(@"^[A-Z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CodePattern();
}
