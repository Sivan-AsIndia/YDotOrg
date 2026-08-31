namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Bound from the JwtSettings section using the options pattern.
///
/// IAM IS THE ONLY SERVICE THAT SIGNS. Every other service in the solution validates the
/// token IAM produced and issues none of its own, so <see cref="Issuer"/>,
/// <see cref="Audience"/> and <see cref="SigningKey"/> have to match the values in each
/// sibling <c>appsettings.json</c> byte for byte. If they drift, every call to the other
/// service returns 401 with a token that is otherwise perfectly valid — a genuinely
/// confusing failure, so it is worth stating plainly here.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Issuer { get; set; } = "YDot.IAM";

    public string Audience { get; set; } = "YDot.Clients";

    /// <summary>
    /// The symmetric signing key. At least 32 characters. Supply it from the environment in
    /// anything that is not a laptop — a key in source control is not a key.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Deliberately short. The access token cannot be revoked directly, so its lifetime is
    /// the window in which a stolen one still works. Fifteen minutes plus a revocable refresh
    /// token is the usual balance.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>How long a refresh token lives, and therefore how long "remember me" lasts.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// Lifetime of the half-authenticated token issued between password and second factor.
    /// Short on purpose: it exists only to carry the user across one screen.
    /// </summary>
    public int MfaPendingTokenMinutes { get; set; } = 10;

    /// <summary>
    /// Lifetime of the token issued to SuperAdmin before they have selected an Organisation.
    /// It carries no Tenant permissions, so it only has to survive the selector screen.
    /// </summary>
    public int TenantSelectionTokenMinutes { get; set; } = 15;

    /// <summary>How long a step-up satisfies the sensitive-action policy before it is asked for again.</summary>
    public int StepUpValidMinutes { get; set; } = 5;

    /// <summary>Tolerance for clock drift between services.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>Name of the HttpOnly cookie the refresh token travels in.</summary>
    public string RefreshTokenCookieName { get; set; } = "ydot_rt";

    /// <summary>
    /// Set false only for local HTTP development. On any real deployment the refresh cookie
    /// must be Secure, or it travels in clear text on the first plain request.
    /// </summary>
    public bool RefreshTokenCookieSecure { get; set; } = true;

    /// <summary>
    /// "None" is required when the API and the client are on different sites, which is the
    /// case for ten1.ngoplanet.com calling api.ngoplanet.com. "None" also forces Secure.
    /// </summary>
    public string RefreshTokenCookieSameSite { get; set; } = "None";

    /// <summary>
    /// Cookie Domain. Setting it to ".ngoplanet.com" lets one refresh cookie work across
    /// every Organisation subdomain. Leave empty to scope the cookie to the exact host,
    /// which is the safer default and the right one in development.
    /// </summary>
    public string? RefreshTokenCookieDomain { get; set; }
}
