using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// An ISO 3166-1 alpha-2 country code: exactly two letters, always upper-cased.
///
/// WHY THIS IS A TYPE RATHER THAN A LENGTH CHECK IN A VALIDATOR. The alpha-2 code is the
/// value that address formatting, phone-number parsing, payment-gateway routing and the
/// country picker all key off. "in", "IN" and " in " are the same country, and if the
/// normalisation lives in whichever validator happens to run then a row inserted by the
/// seeder, by an import and by the create screen can end up with three different spellings
/// of one code - and the unique index will happily accept all three.
///
/// Parsing here means there is exactly one spelling of a country code in the database,
/// whatever route it arrived by.
/// </summary>
public sealed partial record IsoAlpha2Value
{
    public const int RequiredLength = 2;

    private IsoAlpha2Value(string value) => Value = value;

    public string Value { get; }

    /// <summary>Returns null rather than throwing, so a validator can turn it into a field message.</summary>
    public static IsoAlpha2Value? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToUpperInvariant();

        return AlphaTwoPattern().IsMatch(normalised) ? new IsoAlpha2Value(normalised) : null;
    }

    public static IsoAlpha2Value Parse(string candidate) =>
        TryParse(candidate)
        ?? throw new ArgumentException($"'{candidate}' is not a valid ISO 3166-1 alpha-2 code.", nameof(candidate));

    /// <summary>
    /// The flag emoji for the code, built from the two regional indicator symbols.
    ///
    /// Returned from the server so the country list renders identically everywhere rather
    /// than depending on each client shipping the same helper - the Angular screen already
    /// had its own copy of this, and two copies of a mapping is one too many.
    /// </summary>
    public string ToFlagEmoji()
    {
        const int regionalIndicatorBase = 0x1F1E6;

        var first = char.ConvertFromUtf32(regionalIndicatorBase + (Value[0] - 'A'));
        var second = char.ConvertFromUtf32(regionalIndicatorBase + (Value[1] - 'A'));

        return first + second;
    }

    public override string ToString() => Value;

    public static implicit operator string(IsoAlpha2Value code) => code.Value;

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex AlphaTwoPattern();
}
