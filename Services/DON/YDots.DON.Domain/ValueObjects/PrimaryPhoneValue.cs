using System.Text.RegularExpressions;

namespace YDots.DON.Domain.ValueObjects;

/// <summary>
/// Section 4 value object. Wraps the primary phone number and holds the single definition of
/// "normalised to E.164", so the validator, the handler and the masking helper all agree.
/// </summary>
public sealed partial record PrimaryPhoneValue
{
    private PrimaryPhoneValue(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>True when the value is already E.164, for example +919876543210.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(Normalise(value));

    /// <summary>Parses a caller supplied number. Returns null when it cannot be normalised.</summary>
    public static PrimaryPhoneValue? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalised = Normalise(value);
        return Pattern().IsMatch(normalised) ? new PrimaryPhoneValue(normalised) : null;
    }

    /// <summary>
    /// Keeps the last four digits and stars the rest. Used everywhere the caller lacks
    /// don.donors.view-sensitive-contact.
    /// </summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 4 ? new string('*', trimmed.Length) : new string('*', trimmed.Length - 4) + trimmed[^4..];
    }

    /// <summary>Strips spaces, dashes and brackets so "+91 98765-43210" becomes "+919876543210".</summary>
    private static string Normalise(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
