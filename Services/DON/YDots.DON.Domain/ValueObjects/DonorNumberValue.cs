using System.Text.RegularExpressions;

namespace YDots.DON.Domain.ValueObjects;

/// <summary>
/// Section 4 value object. Wraps the DonorNumber string so the format DON-YYYY-NNNNNN is
/// checked in one place instead of being re-checked in every handler.
/// </summary>
public sealed partial record DonorNumberValue
{
    public const string Prefix = "DON";

    private DonorNumberValue(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>Builds the next number for a year from the running sequence, for example DON-2026-000184.</summary>
    public static DonorNumberValue Create(int year, int sequence) =>
        new($"{Prefix}-{year:0000}-{sequence:000000}");

    /// <summary>True when the supplied text already looks like a donor number.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim());

    /// <summary>Parses an existing value. Returns null when the text does not match the format.</summary>
    public static DonorNumberValue? TryParse(string? value) =>
        IsValid(value) ? new DonorNumberValue(value!.Trim().ToUpperInvariant()) : null;

    [GeneratedRegex(@"^DON-\d{4}-\d{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
