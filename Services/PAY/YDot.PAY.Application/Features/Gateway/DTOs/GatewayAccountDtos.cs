namespace YDot.PAY.Application.Features.Gateway.DTOs;

/// <summary>
/// Configuring an organisation's payment gateway account.
///
/// NO SECRET IS ACCEPTED HERE. The request carries the REFERENCE to a secret already placed in
/// the secret store, never the key itself: a merchant secret arriving in a request body ends up
/// in a request log, a proxy buffer and an exception message.
/// </summary>
public sealed record UpsertGatewayAccountRequest(
    string GatewayName,
    string MerchantId,
    string SettlementCurrencyCode,
    string? ApiKeyReference = null,
    string? WebhookSecretReference = null,
    bool IsTestMode = true,
    bool IsActive = true,
    string? ReturnUrl = null,
    string? WebhookUrl = null,
    int PaymentLinkValidityMinutes = 60,

    /// <summary>Comma-separated PaymentMethodType values this account accepts.</summary>
    string? EnabledMethods = null,

    string? Notes = null,
    long? ExpectedVersion = null);

/// <summary>
/// A gateway account as the configuration screen shows it.
///
/// THE SECRET REFERENCES ARE RETURNED, THE SECRETS ARE NOT. Showing that a key is configured is
/// useful; showing the key would put it in the browser's memory, its cache and its dev tools.
/// </summary>
public sealed record GatewayAccountResponse(
    Guid Id,
    Guid TenantId,
    string GatewayName,
    string MerchantId,
    string SettlementCurrencyCode,

    /// <summary>True when a key reference is configured. The key itself never leaves the server.</summary>
    bool HasApiKey,

    bool HasWebhookSecret,

    /// <summary>
    /// Surfaced prominently, because a test account that looks live is how an organisation ends
    /// up reporting income it never received.
    /// </summary>
    bool IsTestMode,

    bool IsActive,
    string? ReturnUrl,
    string? WebhookUrl,
    int PaymentLinkValidityMinutes,
    IReadOnlyList<string> EnabledMethods,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version,
    IReadOnlyList<string> PermittedActions);
