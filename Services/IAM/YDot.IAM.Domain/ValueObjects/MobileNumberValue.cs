using System.Globalization;
using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// An E.164 mobile number held as its two halves: the country prefix ("+91") and the
/// subscriber digits ("9876543210"), matching the <c>MobileCountryCode</c> and
/// <c>MobileNumber</c> columns in the section 3.1 property contract.
///
/// They are kept apart rather than stored as one string because the two are used
/// differently: SMS delivery needs the joined E.164 form, whereas a screen usually wants
/// to show the national number with the country picked separately.
/// </summary>
public sealed partial record MobileNumberValue
{
    private MobileNumberValue(string countryCode, string number)
    {
        CountryCode = countryCode;
        Number = number;
    }

    /// <summary>Country prefix including the plus sign, for example "+91".</summary>
    public string CountryCode { get; }

    /// <summary>Subscriber digits with no spaces or separators.</summary>
    public string Number { get; }

    /// <summary>The joined E.164 form, for example "+919876543210".</summary>
    public string E164 => CountryCode + Number;

    public static MobileNumberValue? TryParse(string? countryCode, string? number)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        // Accept "91" and "+91" alike; store the canonical "+91".
        var normalisedCode = countryCode.Trim();
        if (!normalisedCode.StartsWith('+'))
        {
            normalisedCode = "+" + normalisedCode;
        }

        // Strip the separators people type: spaces, hyphens, brackets.
        var normalisedNumber = new string([.. number.Where(char.IsDigit)]);

        if (!CountryCodePattern().IsMatch(normalisedCode))
        {
            return null;
        }

        // E.164 caps the whole thing at 15 digits including the country code.
        var totalDigits = normalisedCode.Length - 1 + normalisedNumber.Length;

        return normalisedNumber.Length is >= 4 and <= 14 && totalDigits <= 15
            ? new MobileNumberValue(normalisedCode, normalisedNumber)
            : null;
    }

    /// <summary>Masked for display: "***3210". Used on the MFA challenge screen, where the
    /// destination has to be recognisable to its owner without being readable to anybody else.</summary>
    public string Masked()
    {
        var tail = Number.Length <= 4 ? Number : Number[^4..];
        return string.Create(CultureInfo.InvariantCulture, $"***{tail}");
    }

    public override string ToString() => E164;

    [GeneratedRegex(@"^\+[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CountryCodePattern();
}
