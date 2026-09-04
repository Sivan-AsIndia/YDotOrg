namespace YDot.IAM.Infrastructure.Configuration;

/// <summary>
/// PAY's <c>pay_gateway_accounts</c> table, seen from IAM, READ-ONLY.
///
/// WHY THIS EXISTS AT ALL. Before the configuration screen, an Organisation's gateway was set up
/// by a background seeder in PAY: one Razorpay row per Organisation, built from the keys in the
/// deployment's own environment. Those rows are live - they are what takes donations today.
///
/// A configuration screen that could not see them would open on "No payment gateway is
/// configured" for an Organisation whose donations are working perfectly, which is not a cosmetic
/// problem: it invites an administrator to set up a second gateway to fix something that was
/// never broken, and it hides the answer to the question the screen exists to answer - where is
/// this Organisation's money actually going?
///
/// SO THE LIST SHOWS BOTH, labelled by where each came from. These rows are never editable here -
/// their credentials live in the deployment's environment and this service cannot read or change
/// them - but they are visible, and an administrator can supersede one by configuring their own.
///
/// READ-ONLY AND EXCLUDED FROM IAM'S MIGRATIONS. PAY owns this table's DDL; two services issuing
/// CREATE TABLE for one table is a race nobody wins. It carries only the columns the list needs,
/// so a column PAY adds is invisible here.
///
/// IT IS THE MIRROR IMAGE OF WHAT PAY DOES with <c>iam_payment_gateway_configurations</c>, and
/// for the same reason: the four services share one PostgreSQL database and differ only by table
/// prefix, so this is a local read rather than a call across the network.
/// </summary>
internal sealed class DeploymentGatewayAccount
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Razorpay, Stripe, PayU. The same vocabulary the configuration screen uses.</summary>
    public string GatewayName { get; set; } = string.Empty;

    public string MerchantId { get; set; } = string.Empty;

    /// <summary>
    /// The NAME of a configuration section, never a credential.
    ///
    /// Shown on screen as the hint, because that is genuinely what identifies this row's
    /// credentials: "the keys deployed under PaymentGateways:Razorpay". It is not a secret - it
    /// is the opposite of one, which is the whole point of the indirection.
    /// </summary>
    public string? ApiKeyReference { get; set; }

    public string? WebhookSecretReference { get; set; }

    public bool IsTestMode { get; set; }

    public bool IsActive { get; set; }

    public string SettlementCurrencyCode { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public string? WebhookUrl { get; set; }

    public int PaymentLinkValidityMinutes { get; set; }

    public string? EnabledMethods { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public long Version { get; set; }
}
