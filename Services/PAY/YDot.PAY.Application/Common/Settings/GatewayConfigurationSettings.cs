namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// How PAY unseals the merchant credentials an Organisation entered on IAM's configuration
/// screen.
///
/// THE SECTION NAME AND THE KEY ARE IAM'S. This is deliberately the same
/// <c>PaymentGatewaySettings</c> section IAM binds, with the same variable name, so one line in
/// the compose file configures both services:
///
/// <code>
/// PaymentGatewaySettings__EncryptionKey = &lt;32 random bytes, base64&gt;
/// </code>
///
/// IF THE TWO SERVICES DERIVE DIFFERENT KEYS, PAY CANNOT UNSEAL WHAT IAM SEALED, and every
/// donation for an Organisation that configured its own gateway is refused with
/// PAYMENT_GATEWAY_NOT_CONFIGURED - a failure that looks like a missing configuration rather
/// than a mismatched key, and therefore sends whoever investigates to the wrong screen. It is
/// the one thing to check first if the configuration page shows a gateway as active and
/// donations still refuse.
///
/// WHEN IT IS BLANK, both services derive the key from <c>JwtSettings:SigningKey</c> by HKDF
/// with the same label - which they already share, because PAY validates the tokens IAM signs.
/// That is what makes the feature work in the existing docker-compose with no new variable, at
/// the cost of tying the two together: rotating the signing key would make every stored merchant
/// credential unreadable.
/// </summary>
public sealed class GatewayConfigurationSettings
{
    /// <summary>Bound from IAM's section name on purpose. See the class comment.</summary>
    public const string SectionName = "PaymentGatewaySettings";

    /// <summary>32 random bytes, base64. Blank falls back to the JWT-derived key.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether a tenant-entered configuration is allowed to override the deployment's own.
    ///
    /// AN ESCAPE HATCH, AND ONE WORTH HAVING. If a configuration screen ever put a bad credential
    /// in front of every donation for an Organisation, this turns the whole mechanism off with a
    /// restart and puts the deployment's configured credentials back in charge - without a
    /// release, and without an administrator having to find and correct the row first.
    /// </summary>
    public bool UseTenantConfiguration { get; set; } = true;
}
