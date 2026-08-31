namespace YDots.DON.Application.Common.Settings;

/// <summary>
/// Bound from the JwtSettings section of appsettings.json using the options pattern.
///
/// DON never creates a token. It only validates the one IAM signed, so Issuer, Audience and
/// SigningKey must be byte-for-byte identical to the IAM values or every call arrives as 401.
/// The token lifetime settings are not repeated here: DON has no say in them.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    /// <summary>Must equal the IAM issuer, "YDot.IAM".</summary>
    public string Issuer { get; set; } = "YDot.IAM";

    /// <summary>Must equal the IAM audience, "YDot.Clients".</summary>
    public string Audience { get; set; } = "YDot.Clients";

    /// <summary>The same symmetric key IAM signs with. Supply it from the environment in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Tolerance for clock drift between the IAM container and this one.</summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
