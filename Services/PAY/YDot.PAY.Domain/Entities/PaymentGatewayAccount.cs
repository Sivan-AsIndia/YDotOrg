using YDot.PAY.Domain.Common;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// One Organisation's payment gateway configuration.
///
/// PER ORGANISATION, NOT PER PLATFORM, and that is what makes the whole module tenant-specific
/// in the way that actually matters: each charity's money goes to its OWN merchant account.
/// A shared gateway account would pool every organisation's donations into one settlement, which
/// is not a data-isolation problem - it is a legal one.
///
/// NO SECRET IS STORED HERE. The API key lives in the secret store and this row holds only its
/// reference. A merchant secret in a database column is readable by anybody with a backup, and
/// backups travel.
/// </summary>
public sealed class PaymentGatewayAccount : TenantEntity
{
    /// <summary>Which provider: Razorpay, Stripe, PayU.</summary>
    public string GatewayName { get; set; } = string.Empty;

    /// <summary>The merchant identifier the provider assigned. Not a secret.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>
    /// The KEY to the secret in the secret store, never the secret itself.
    ///
    /// See the class comment: a merchant secret in a database column is readable by anybody
    /// holding a backup.
    /// </summary>
    public string? ApiKeyReference { get; set; }

    /// <summary>The key to the webhook signing secret, again by reference only.</summary>
    public string? WebhookSecretReference { get; set; }

    /// <summary>
    /// Whether this account is in test mode.
    ///
    /// SURFACED ON EVERY SCREEN THAT SHOWS MONEY. A test donation that looks real in a report is
    /// how a charity ends up reporting income it never received.
    /// </summary>
    public bool IsTestMode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The currency this account settles in. Must match what the campaign asks for.</summary>
    public string SettlementCurrencyCode { get; set; } = string.Empty;

    /// <summary>Where the gateway sends the donor back to. Recorded so it can be verified.</summary>
    public string? ReturnUrl { get; set; }

    public string? WebhookUrl { get; set; }

    /// <summary>How long a payment link stays valid, in minutes.</summary>
    public int PaymentLinkValidityMinutes { get; set; } = 60;

    /// <summary>The methods this account accepts, as a comma-separated list of PaymentMethodType.</summary>
    public string? EnabledMethods { get; set; }

    public string? Notes { get; set; }
}
