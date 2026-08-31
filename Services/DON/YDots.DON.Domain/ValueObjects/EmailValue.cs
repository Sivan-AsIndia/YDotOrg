using System.Text.RegularExpressions;

namespace YDots.DON.Domain.ValueObjects;

/// <summary>
/// Wraps the primary e-mail address. Not listed in section 4, but the property contract says
/// "Valid email when supplied" and masking works exactly like the phone number, so the rule
/// belongs in one place beside its sibling.
/// </summary>
public sealed partial record EmailValue
{
    private EmailValue(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim());

    public static EmailValue? TryParse(string? value) =>
        IsValid(value) ? new EmailValue(value!.Trim().ToLowerInvariant()) : null;

    /// <summary>Turns "arun.kumar@example.com" into "ar***@example.com".</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0)
        {
            return new string('*', trimmed.Length);
        }

        var local = trimmed[..at];
        var domain = trimmed[at..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];

        return visible + "***" + domain;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
