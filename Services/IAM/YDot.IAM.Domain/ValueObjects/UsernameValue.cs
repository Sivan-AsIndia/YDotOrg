using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// A validated, normalised username: 3–64 characters, letters, digits, dot, hyphen and
/// underscore. Lower-cased on construction for the same reason as <see cref="EmailValue"/>
/// — so the scoped-unique index actually catches duplicates.
///
/// Like the e-mail, uniqueness is per Tenant, not per platform.
/// </summary>
public sealed partial record UsernameValue
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 64;

    private UsernameValue(string value) => Value = value;

    public string Value { get; }

    public static UsernameValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToLowerInvariant();

        return normalised.Length is >= MinimumLength and <= MaximumLength && UsernamePattern().IsMatch(normalised)
            ? new UsernameValue(normalised)
            : null;
    }

    public static UsernameValue Parse(string candidate) =>
        TryParse(candidate) ?? throw new ArgumentException($"'{candidate}' is not a valid username.", nameof(candidate));

    public override string ToString() => Value;

    public static implicit operator string(UsernameValue username) => username.Value;

    [GeneratedRegex(@"^[a-z0-9._-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex UsernamePattern();
}
