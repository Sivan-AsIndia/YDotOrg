using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities.Configuration;

/// <summary>
/// One Organisation's payment gateway credentials, as entered on the configuration screen.
///
/// PER ORGANISATION, AND THAT IS THE POINT. Each charity's donations settle into its OWN
/// merchant account. A shared account would pool several organisations' income into one
/// settlement, which is a legal problem rather than a data one, and no amount of correct
/// reporting afterwards would fix it.
///
/// THE CREDENTIALS ARE ENCRYPTED, NOT STORED IN CLEAR AND NOT MERELY REFERENCED.
///
/// PAY's own <c>pay_gateway_accounts</c> table holds the NAME of a configuration section and
/// resolves the key from the environment at the moment of use, which is the stronger design: a
/// stolen database backup yields no credential at all. It also requires a deployment change per
/// organisation, which is precisely what this screen exists to remove - a TenantAdmin who has
/// just been issued a Razorpay key needs somewhere to put it without waiting on a release.
///
/// So the compromise is deliberate, and its limits are worth stating plainly:
///
///   * The key and secret are sealed with AES-256-GCM before they reach a column, so a database
///     dump or a stolen backup on its own decrypts to nothing.
///   * The sealing key lives in configuration, NOT in this database. Reading a credential
///     therefore needs both the data and the deployment's secrets, which is the property an
///     encrypted-column scheme keeping its key in the same database does not have.
///   * NO CIPHERTEXT EVER LEAVES THE API. Responses carry <see cref="ApiKeyHint"/> and the
///     has-a-secret flags, never the sealed bytes and never the plaintext.
///
/// WHAT IS AND IS NOT A SECRET HERE. <see cref="MerchantId"/>, <see cref="WebhookUrl"/> and the
/// provider name are identifiers the provider prints on its own dashboard; they are stored in
/// clear and shown in full. Everything ending in <c>Cipher</c> is sealed and never shown.
/// </summary>
public sealed class PaymentGatewayConfiguration : TenantEntity
{
    /// <summary>Which provider this row configures. Matched against PAY's adapter names.</summary>
    public PaymentGatewayProvider Provider { get; set; } = PaymentGatewayProvider.None;

    /// <summary>
    /// Sandbox or Production. Half of the natural key, because an Organisation legitimately
    /// holds one of each for the same provider while it is being set up.
    /// </summary>
    public PaymentGatewayEnvironment Environment { get; set; } = PaymentGatewayEnvironment.Sandbox;

    /// <summary>What an operator calls this row. Falls back to the provider name when blank.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The merchant identifier the provider assigned. Not a secret.</summary>
    public string? MerchantId { get; set; }

    // ---- Credentials. Sealed, never returned, never logged. ---------------------------------

    /// <summary>The API key or publishable key, sealed. Null until one is entered.</summary>
    public string? ApiKeyCipher { get; set; }

    /// <summary>
    /// The last few characters of the API key, in clear.
    ///
    /// A HINT IS NOT A LEAK AND IT EARNS ITS PLACE. Without one the screen can say only "a key
    /// is set", which leaves an operator staring at a failing gateway unable to tell whether the
    /// key in the box is the one their provider dashboard shows. Four characters cannot be
    /// worked back into a credential, and the prefix - <c>rzp_test_</c> against <c>rzp_live_</c>
    /// - is the part that actually answers the question being asked.
    /// </summary>
    public string? ApiKeyHint { get; set; }

    /// <summary>The secret half of the credential pair, sealed.</summary>
    public string? SecretKeyCipher { get; set; }

    /// <summary>Whether a secret is present, so the screen can say so without unsealing it.</summary>
    public bool HasSecretKey { get; set; }

    // ---- Webhook -----------------------------------------------------------------------------

    /// <summary>Where the provider posts payment callbacks. Not a secret; it is a public URL.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>The webhook signing secret, sealed.</summary>
    public string? WebhookSecretCipher { get; set; }

    public bool HasWebhookSecret { get; set; }

    /// <summary>
    /// The provider events this Organisation wants delivered, comma separated.
    ///
    /// Text rather than a child table because it is read as a whole, written as a whole, and
    /// never joined against. Rows here would buy nothing but a join.
    /// </summary>
    public string? SubscribedEvents { get; set; }

    // ---- Settlement --------------------------------------------------------------------------

    /// <summary>The currency this account settles in. Must match what the campaign asks for.</summary>
    public string SettlementCurrencyCode { get; set; } = "INR";

    /// <summary>Where the provider sends the donor back to.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>How long a payment link stays valid, in minutes.</summary>
    public int PaymentLinkValidityMinutes { get; set; } = 60;

    /// <summary>Accepted methods, comma separated. Empty means the provider's own default set.</summary>
    public string? EnabledMethods { get; set; }

    // ---- State ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether donations should be taken through this row.
    ///
    /// AT MOST ONE ACTIVE ROW PER ORGANISATION PER ENVIRONMENT, enforced by the command handler
    /// rather than by a unique index: the transition is "activate this one and stand the others
    /// down", and a partial index would turn that into a two-step the database could interrupt
    /// halfway.
    /// </summary>
    public bool IsActive { get; set; }

    // ---- Last test -------------------------------------------------------------------------------

    public DateTimeOffset? LastTestedAtUtc { get; set; }

    /// <summary>True when the last Test reached the provider and it accepted the credentials.</summary>
    public bool? LastTestSucceeded { get; set; }

    /// <summary>What the provider said, or why the attempt never got that far. Never a secret.</summary>
    public string? LastTestMessage { get; set; }

    public string? Notes { get; set; }
}
