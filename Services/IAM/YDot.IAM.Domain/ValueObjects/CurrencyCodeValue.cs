using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// An ISO 4217 currency code: exactly three letters, always upper-cased.
///
/// THE SAME ARGUMENT AS <see cref="IsoAlpha2Value"/>, WITH MONEY ATTACHED. A donation row,
/// a payment request and a receipt each carry a currency code, and they are compared as
/// strings when the three are reconciled. One of them holding "inr" while the others hold
/// "INR" turns a matched transaction into an exception on somebody's desk.
///
/// Crypto and internal units are deliberately accepted: the format check is three letters,
/// not membership of the ISO list, so an Organisation can add XBT or a voucher unit without
/// the type refusing it. Whether a code is a real ISO currency is a question for the
/// catalogue, not for the string.
/// </summary>
public sealed partial record CurrencyCodeValue
{
    public const int RequiredLength = 3;

    private CurrencyCodeValue(string value) => Value = value;

    public string Value { get; }

    /// <summary>Returns null rather than throwing, so a validator can turn it into a field message.</summary>
    public static CurrencyCodeValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToUpperInvariant();

        return CurrencyPattern().IsMatch(normalised) ? new CurrencyCodeValue(normalised) : null;
    }

    public static CurrencyCodeValue Parse(string candidate) =>
        TryParse(candidate)
        ?? throw new ArgumentException($"'{candidate}' is not a valid currency code.", nameof(candidate));

    public override string ToString() => Value;

    public static implicit operator string(CurrencyCodeValue code) => code.Value;

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CurrencyPattern();
}
