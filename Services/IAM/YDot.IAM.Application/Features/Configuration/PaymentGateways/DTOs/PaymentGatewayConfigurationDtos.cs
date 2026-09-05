using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;

/// <summary>
/// What the configuration form sends when it saves.
///
/// AN UPSERT RATHER THAN A POST AND A PUT, because the natural key is (Organisation, provider,
/// environment) rather than an id the browser holds. A screen that had to know whether a row
/// already existed would have to ask first, and the answer could change between the two calls.
///
/// THE THREE CREDENTIAL FIELDS ARE WRITE-ONLY AND THEIR NULL IS MEANINGFUL. Null means "leave
/// whatever is stored alone" - which is what the form sends when somebody edits the webhook URL
/// on a configuration whose key they never touched, because the form was never given the key to
/// send back. Empty string means "clear it". Getting that distinction wrong in either direction
/// is how an unrelated edit silently wipes a working credential.
/// </summary>
public sealed record UpsertPaymentGatewayConfigurationRequest
{
    /// <summary>Absent on a create. Present, and matched with <see cref="ExpectedVersion"/>, on an edit.</summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// The Organisation this configuration belongs to.
    ///
    /// IGNORED FOR EVERYBODY BUT SUPERADMIN, and the handler enforces that rather than trusting
    /// it. A TenantAdmin's Organisation comes from their token; honouring a body field here
    /// would let one Organisation configure another's merchant account, which is the single
    /// worst thing this endpoint could be talked into.
    /// </summary>
    public Guid? TenantId { get; init; }

    public PaymentGatewayProvider Provider { get; init; } = PaymentGatewayProvider.None;

    public PaymentGatewayEnvironment Environment { get; init; } = PaymentGatewayEnvironment.Sandbox;

    public string? DisplayName { get; init; }

    public string? MerchantId { get; init; }

    /// <summary>The public half of the credential pair. Null leaves the stored one alone.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The secret half. Null leaves the stored one alone.</summary>
    public string? SecretKey { get; init; }

    public string? WebhookUrl { get; init; }

    /// <summary>The webhook signing secret. Null leaves the stored one alone.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>Event codes from the catalogue. Null is treated as "no change" on an edit.</summary>
    public IReadOnlyList<string>? SubscribedEvents { get; init; }

    public string SettlementCurrencyCode { get; init; } = "INR";

    public string? ReturnUrl { get; init; }

    public int PaymentLinkValidityMinutes { get; init; } = 60;

    /// <summary>Method codes from the catalogue. Empty means the merchant account's own set.</summary>
    public IReadOnlyList<string>? EnabledMethods { get; init; }

    /// <summary>
    /// Whether donations should go through this row.
    ///
    /// Activating one stands the others in the same environment down, in the same transaction.
    /// </summary>
    public bool IsActive { get; init; }

    public string? Notes { get; init; }

    /// <summary>
    /// The version the form loaded. Absent means create; present means "update THIS version".
    ///
    /// A stale one answers 409 rather than overwriting whatever somebody else saved in between -
    /// which on this screen would mean silently re-pointing an Organisation's settlement account
    /// back to the previous provider.
    /// </summary>
    public long? ExpectedVersion { get; init; }

    /// <summary>Why. Recorded on the change log and on the platform audit row.</summary>
    public string? Reason { get; init; }
}

/// <summary>Turns a configuration on or off without touching anything else.</summary>
public sealed record ChangePaymentGatewayStatusRequest
{
    public bool IsActive { get; init; }

    public long ExpectedVersion { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Deletes a configuration. The change log survives it.</summary>
public sealed record DeletePaymentGatewayConfigurationRequest
{
    public long ExpectedVersion { get; init; }

    /// <summary>Required. Removing where an Organisation's money settles needs a stated reason.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// One configuration, as a screen sees it.
///
/// NO CIPHERTEXT AND NO PLAINTEXT CREDENTIAL APPEARS ON THIS TYPE. What it carries instead is
/// <see cref="ApiKeyHint"/> and three booleans, which is enough for the form to show "a secret
/// is set" and for an operator to recognise which key is in the box. A secret in a response ends
/// up in the browser's memory, in its dev tools, and in any proxy that logged the body.
/// </summary>
public sealed record PaymentGatewayConfigurationResponse(
    Guid Id,
    Guid TenantId,
    string? OrganisationName,
    string? OrganisationCode,
    string Provider,
    string ProviderName,
    string Environment,
    string? DisplayName,
    string? MerchantId,
    string? ApiKeyHint,
    bool HasApiKey,
    bool HasSecretKey,
    string? WebhookUrl,
    bool HasWebhookSecret,
    IReadOnlyList<string> SubscribedEvents,
    string SettlementCurrencyCode,
    string? ReturnUrl,
    int PaymentLinkValidityMinutes,
    IReadOnlyList<string> EnabledMethods,
    bool IsActive,
    // False when PAY has no adapter for this provider. The screen warns rather than hides.
    bool IsAdapterAvailable,
    DateTimeOffset? LastTestedAtUtc,
    bool? LastTestSucceeded,
    string? LastTestMessage,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version,
    // What THIS caller may do next. Render buttons from it, not from local rules.
    IReadOnlyList<string> PermittedActions,

    // WHERE THIS ROW CAME FROM: "Configured" for one entered on this screen, "Deployment" for a
    // pay_gateway_accounts row the payments service built from the environment.
    //
    // THE LIST SHOWS BOTH, and it has to. Every organisation on this platform already had a
    // gateway before this screen existed; a list that showed only its own rows would open on
    // "nothing configured" for an organisation taking donations perfectly well, and invite
    // somebody to fix what was never broken.
    string Source = ConfigurationSources.Configured,

    // True when a Deployment row is no longer the one taking money, because the organisation has
    // since configured its own and made it active. Shown greyed rather than hidden: an operator
    // asking "why did the merchant account change?" needs to see that the old one is still there
    // and is simply no longer in use.
    bool IsSuperseded = false,

    // The credential hint for a Deployment row is the NAME of the configuration section its keys
    // are deployed under - "PaymentGateways:Razorpay". That is not a secret; it is the opposite
    // of one, and it is what identifies the credentials to whoever maintains the environment.
    string? DeploymentKeyReference = null);

/// <summary>Where a row on the list came from. Two values, and they behave differently.</summary>
public static class ConfigurationSources
{
    /// <summary>Entered on this screen. Editable, testable, deletable.</summary>
    public const string Configured = "Configured";

    /// <summary>
    /// Built by the payments service from the deployment's environment. READ-ONLY here: its
    /// credentials live in the environment, and this service can neither read nor change them.
    /// The way to take one over is to configure your own, which supersedes it.
    /// </summary>
    public const string Deployment = "Deployment";
}

/// <summary>What the form needs before anybody has chosen a provider.</summary>
public sealed record PaymentGatewayCatalogueResponse(
    IReadOnlyList<PaymentGatewayProviderOption> Providers,
    IReadOnlyList<PaymentGatewayEventOption> WebhookEvents,
    IReadOnlyList<PaymentGatewayMethodOption> PaymentMethods,
    // The webhook address this deployment expects a provider to call, with "{provider}" where
    // the provider's own name goes - PAY reads the provider from the ROUTE so it knows which
    // secret to check the signature against before it trusts a byte of the body.
    //
    // BUILT FROM THE CONFIGURED PUBLIC ADDRESS RATHER THAN GUESSED, because the usual outcome
    // otherwise is somebody pasting localhost into a provider dashboard and losing a day to
    // webhooks that never arrive.
    string? WebhookUrlTemplate);

/// <summary>One provider on the dropdown.</summary>
public sealed record PaymentGatewayProviderOption(
    string Code,
    string Name,
    bool HasAdapter,
    string ApiKeyLabel,
    string? SecretKeyLabel,
    string? MerchantIdLabel,
    string? TestKeyPrefix,
    string? LiveKeyPrefix,
    string? DocumentationUrl);

public sealed record PaymentGatewayEventOption(string Code, string Name, string Description);

public sealed record PaymentGatewayMethodOption(string Code, string Name);

/// <summary>
/// One line of the change log, as the audit panel renders it.
///
/// OLD AND NEW ARE MASKED FOR A CREDENTIAL, at the point they are written rather than here.
/// This type simply never receives a secret to withhold.
/// </summary>
public sealed record PaymentGatewayConfigurationAuditResponse(
    Guid Id,
    Guid ConfigurationId,
    Guid TenantId,
    string? OrganisationName,
    string Provider,
    string Environment,
    string Action,
    string? FieldName,
    // A readable label for FieldName: "Webhook URL" rather than "WebhookUrl".
    string? FieldLabel,
    string? OldValue,
    string? NewValue,
    Guid? ActorUserId,
    string? ActorDisplayName,
    DateTimeOffset OccurredAtUtc,
    string? Reason,
    string? IpAddress,
    string? CorrelationId);

/// <summary>What the Test button gets back.</summary>
public sealed record PaymentGatewayTestResultResponse(
    Guid ConfigurationId,
    string Provider,
    string Environment,
    bool Succeeded,
    string Message,
    // What the provider created, where it created something. A Razorpay order id.
    string? Reference,
    long DurationMilliseconds,
    DateTimeOffset TestedAtUtc);

/// <summary>How the list of configurations may be narrowed.</summary>
public sealed record PaymentGatewayConfigurationFilter
{
    /// <summary>
    /// SuperAdmin only: one Organisation's configurations. Ignored for everybody else, whose
    /// scope comes from their token.
    /// </summary>
    public Guid? TenantId { get; init; }

    public PaymentGatewayProvider? Provider { get; init; }

    public PaymentGatewayEnvironment? Environment { get; init; }

    public bool? IsActive { get; init; }

    /// <summary>Matches the display name, the merchant id and the Organisation name.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>How the change log may be narrowed.</summary>
public sealed record PaymentGatewayAuditFilter
{
    /// <summary>One configuration's history. Absent means every configuration in scope.</summary>
    public Guid? ConfigurationId { get; init; }

    /// <summary>SuperAdmin only, as above.</summary>
    public Guid? TenantId { get; init; }

    public PaymentGatewayConfigurationAction? Action { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
