namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// IAM's payment gateway configuration table, seen from PAY, READ-ONLY.
///
/// WHY PAY READS ANOTHER MODULE'S TABLE, WHICH IS NOT SOMETHING TO DO CASUALLY. The
/// configuration screen lives in IAM because it is administrative configuration attached to an
/// Organisation, and the credentials it holds are needed on the donation path, which lives here.
/// The three ways to bridge that were:
///
///   1. An HTTP call from PAY to IAM on every payment. It puts a second service in the critical
///      path of taking money: IAM restarting during a deployment would refuse donations, and
///      IAM being slow would make the donor's page slow.
///   2. Copying the configuration into pay_* on every save. Two copies of a merchant credential
///      is one more than the number that can be revoked in one action, and the copy drifts the
///      first time a write fails after the first half committed.
///   3. Reading the table directly. The four services already share ONE PostgreSQL database and
///      differ only by table prefix, so this is a local join, not a distributed one.
///
/// The third is what this is. Its cost is a coupling to IAM's schema, and the mitigations are
/// that this type is mapped read-only, is EXCLUDED FROM PAY'S MIGRATIONS - IAM owns the DDL and
/// two services issuing CREATE TABLE for one table is a race nobody wins - and carries only the
/// columns the payment path actually needs. A column IAM adds is invisible here; a column IAM
/// removes fails loudly on the next query rather than silently returning nulls.
///
/// THE CREDENTIAL COLUMNS ARRIVE SEALED and are unsealed by
/// <see cref="TenantConfiguredCredentialResolver"/> with the same key IAM sealed them with. They
/// are never logged, never written to a payment record, and never returned from an endpoint.
/// </summary>
internal sealed class TenantGatewayConfiguration
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid BusinessUnitId { get; set; }

    /// <summary>
    /// The provider name as IAM stores it: Razorpay, Stripe, PayPal.
    ///
    /// It is written into <c>PaymentGatewayAccount.GatewayName</c> by the overlay, which is what
    /// <see cref="PaymentGatewayRouter"/> dispatches on - so an Organisation that switches
    /// provider on the configuration screen starts speaking that provider's API with no
    /// deployment.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Sandbox or Production, as text.</summary>
    public string Environment { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? MerchantId { get; set; }

    // ---- Sealed. See the class comment. ------------------------------------------------------

    public string? ApiKeyCipher { get; set; }

    public string? SecretKeyCipher { get; set; }

    public string? WebhookSecretCipher { get; set; }

    public string? WebhookUrl { get; set; }

    public string SettlementCurrencyCode { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public int PaymentLinkValidityMinutes { get; set; }

    public string? EnabledMethods { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>True when this row is the Organisation's production configuration.</summary>
    public bool IsProduction =>
        string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);
}
