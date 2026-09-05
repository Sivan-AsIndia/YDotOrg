namespace YDot.IAM.Application.Features.Configuration.PaymentGateways;

/// <summary>
/// The field names the change log records, their readable labels, and - the part that matters -
/// which of them are credentials.
///
/// WHY THE MASK LIST LIVES HERE RATHER THAN AT EACH CALL SITE. The audit writer masks by field
/// name, so "is this a secret?" is answered in exactly one place. The alternative - each caller
/// remembering to pass a masked value - is the arrangement where the first person to add a
/// credential field writes its plaintext into the audit table, and nobody notices until an
/// auditor reads it.
///
/// A NAME NOT LISTED IN <see cref="Masked"/> IS TREATED AS SAFE, which is the wrong default in
/// principle. It is the right one here because the set of fields is closed and small, listed in
/// <see cref="Labels"/> beside this, and every one of them is either in the mask list or is
/// something the provider prints on its own dashboard.
/// </summary>
public static class PaymentGatewayFields
{
    public const string Provider = "Provider";
    public const string Environment = "Environment";
    public const string DisplayName = "DisplayName";
    public const string MerchantId = "MerchantId";
    public const string ApiKey = "ApiKey";
    public const string SecretKey = "SecretKey";
    public const string WebhookUrl = "WebhookUrl";
    public const string WebhookSecret = "WebhookSecret";
    public const string SubscribedEvents = "SubscribedEvents";
    public const string SettlementCurrencyCode = "SettlementCurrencyCode";
    public const string ReturnUrl = "ReturnUrl";
    public const string PaymentLinkValidityMinutes = "PaymentLinkValidityMinutes";
    public const string EnabledMethods = "EnabledMethods";
    public const string IsActive = "IsActive";
    public const string Notes = "Notes";

    /// <summary>
    /// The credentials. A change to one of these records "set", "changed" or "cleared" and the
    /// four-character hint - never the value, in either the old or the new column.
    /// </summary>
    public static readonly IReadOnlySet<string> Masked =
        new HashSet<string>(StringComparer.Ordinal) { ApiKey, SecretKey, WebhookSecret };

    /// <summary>What the audit panel shows in its Field column.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Provider] = "Provider",
            [Environment] = "Environment",
            [DisplayName] = "Display name",
            [MerchantId] = "Merchant ID",
            [ApiKey] = "API key",
            [SecretKey] = "Secret key",
            [WebhookUrl] = "Webhook URL",
            [WebhookSecret] = "Webhook secret token",
            [SubscribedEvents] = "Webhook events",
            [SettlementCurrencyCode] = "Settlement currency",
            [ReturnUrl] = "Return URL",
            [PaymentLinkValidityMinutes] = "Payment link validity",
            [EnabledMethods] = "Payment methods",
            [IsActive] = "Status",
            [Notes] = "Notes"
        };

    public static bool IsMasked(string? fieldName) =>
        fieldName is not null && Masked.Contains(fieldName);

    /// <summary>The label for a field, falling back to the raw name for anything unlisted.</summary>
    public static string? LabelFor(string? fieldName) =>
        fieldName is null
            ? null
            : Labels.TryGetValue(fieldName, out var label) ? label : fieldName;
}
