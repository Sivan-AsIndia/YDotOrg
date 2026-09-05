namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// How merchant credentials entered on the configuration screen are sealed, and how long the
/// Test button is allowed to wait.
///
/// THE ENCRYPTION KEY IS THE WHOLE POINT OF THIS CLASS, so it is worth being exact about where
/// it comes from and what happens when it is absent.
///
/// SET IT EXPLICITLY IN ANY REAL DEPLOYMENT:
///
/// <code>
/// PaymentGatewaySettings__EncryptionKey = &lt;32 random bytes, base64&gt;
/// </code>
///
/// Generate one with <c>openssl rand -base64 32</c>. It belongs in the same place as the
/// database password and the JWT signing key - the environment, a mounted secret, a vault - and
/// NEVER in appsettings.json, which travels inside the image.
///
/// WHEN IT IS BLANK, THE KEY IS DERIVED FROM THE JWT SIGNING KEY instead, and the service logs a
/// warning at startup. That fallback is not laziness; it is what makes the feature work on a
/// developer's machine and in the existing docker-compose without a new variable, and it is safe
/// enough to be worth having because the JWT signing key is already a high-entropy secret held
/// in configuration rather than in the database. It has one real cost, and it is the reason for
/// the warning: ROTATING THE JWT SIGNING KEY WOULD MAKE EVERY STORED CREDENTIAL UNREADABLE. With
/// an explicit key the two rotate independently.
///
/// PAY MUST DERIVE THE SAME KEY, whichever route is taken, or it cannot unseal what this service
/// sealed and every donation is refused with PAYMENT_GATEWAY_NOT_CONFIGURED. Both services read
/// the same two configuration values from the same compose file, which is what makes that work.
/// </summary>
public sealed class PaymentGatewaySettings
{
    public const string SectionName = "PaymentGatewaySettings";

    /// <summary>
    /// The sealing key: 32 random bytes, base64. Blank falls back to the derivation described
    /// above.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// How long the Test button waits for a provider before giving up.
    ///
    /// SHORT ON PURPOSE. Somebody is watching a spinner, and a gateway that takes more than ten
    /// seconds to answer an authenticated call has told you what you needed to know.
    /// </summary>
    public int TestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Whether the Test button may reach the internet at all.
    ///
    /// FOR THE ENVIRONMENTS WITH NO EGRESS - a locked-down CI box, an air-gapped demo - where
    /// every test would otherwise fail with a connection error that reads like a broken feature.
    /// With this false the button reports what it can check locally and says plainly that it did
    /// not contact the provider.
    /// </summary>
    public bool AllowOutboundTest { get; set; } = true;
}
