using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Resolves a gateway credential from the Organisation's own configuration first, and from the
/// deployment's configuration second.
///
/// THE TWO SOURCES, AND WHY BOTH EXIST.
///
/// The deployment's configuration - <c>PaymentGateways:{reference}:ApiKey</c>, resolved by
/// <see cref="ConfigurationGatewayCredentialResolver"/> - is the stronger arrangement and is
/// unchanged: the database holds a NAME, the key lives in the environment, and a stolen backup
/// yields no credential at all. It costs a deployment per Organisation, which is exactly what
/// the configuration screen exists to remove.
///
/// The Organisation's own configuration is sealed in IAM's table and opened here. Weaker, and
/// deliberately so - see the entity's own comment in IAM for the full argument - but it lets a
/// TenantAdmin who has just been issued a merchant key put it somewhere without waiting on a
/// release.
///
/// THE ORDER IS TENANT FIRST, DEPLOYMENT SECOND, and it has to be that way round. An
/// Organisation that has explicitly entered its own credentials has said where its money goes,
/// and a deployment default silently winning over that would settle donations into whichever
/// merchant account the environment happened to name - which for a shared default is somebody
/// else's.
///
/// A FALL-THROUGH IS SILENT AND IS MEANT TO BE. An Organisation with no configuration, or one
/// whose credential cannot be opened, gets the deployment's answer - which is how every donation
/// on this platform was taken before this feature existed. The unsealer logs the one case worth
/// investigating, a credential that is present but will not open.
/// </summary>
internal sealed class TenantConfiguredCredentialResolver(
    ConfigurationGatewayCredentialResolver fallback,
    TenantGatewayConfigurationReader configurations,
    GatewayCredentialUnsealer unsealer,
    IOptions<GatewayConfigurationSettings> settings,
    IConfiguration deploymentConfiguration,
    ILogger<TenantConfiguredCredentialResolver> logger) : IGatewayCredentialResolver
{
    /// <summary>
    /// The marker written into <c>ApiKeyReference</c> by
    /// <see cref="ConfiguredGatewayAccountRepository"/>.
    ///
    /// WHY A MARKER RATHER THAN A NULL. Null already means "no credential configured" and is
    /// reported to a donor as PAYMENT_GATEWAY_NOT_CONFIGURED; reusing it would make an
    /// Organisation that HAS configured a gateway indistinguishable from one that has not. The
    /// prefix says "look in the tenant configuration", and the configuration id after it makes a
    /// log line traceable to the row it came from.
    /// </summary>
    private const string ReferencePrefix = "tenant-config:";

    private readonly GatewayConfigurationSettings _settings = settings.Value;

    /// <summary>The marker for one configuration.</summary>
    public static string ReferenceFor(TenantGatewayConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return ReferencePrefix + configuration.Id.ToString("N");
    }

    public GatewayCredential? Resolve(PaymentGatewayAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!_settings.UseTenantConfiguration)
        {
            return fallback.Resolve(account);
        }

        var configuration = configurations.GetActive(account.TenantId);

        if (configuration is null)
        {
            return fallback.Resolve(account);
        }

        var credential = Build(configuration);

        if (credential is not null)
        {
            return credential;
        }

        // The Organisation has a configuration but no usable credential in it - blank, or sealed
        // with a key this service cannot derive. Falling back rather than refusing is what keeps
        // a stack whose keys are in the environment working while somebody half-fills the screen.
        logger.LogWarning(
            "Organisation {TenantId} has an active {Provider} gateway configuration, but no "
            + "usable credential could be read from it. Falling back to this deployment's own "
            + "configured credentials.",
            account.TenantId,
            configuration.Provider);

        return fallback.Resolve(account);
    }

    /// <summary>
    /// Turns a configuration row into the credential the adapters want.
    ///
    /// THE API KEY IS ASSEMBLED AS <c>key:secret</c>, which is the shape
    /// <see cref="RazorpayGateway"/> splits on for HTTP Basic authentication and for handing the
    /// key id - the half that may be published - to Checkout in the browser. The secret after the
    /// colon never leaves this process.
    /// </summary>
    private GatewayCredential? Build(TenantGatewayConfiguration configuration)
    {
        var apiKey = unsealer.Unseal(configuration.ApiKeyCipher);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var secret = unsealer.Unseal(configuration.SecretKeyCipher);

        var composed = string.IsNullOrWhiteSpace(secret) ? apiKey : $"{apiKey}:{secret}";

        var webhookSecret = unsealer.Unseal(configuration.WebhookSecretCipher);

        var baseUrl = BaseUrlFor(configuration);

        // THE GENERIC ADAPTER HAS NO DEFAULT ADDRESS TO FALL BACK ON, unlike the provider
        // adapters: "hosted checkout" is whatever endpoint the deployment stood up, so a blank
        // base URL there is not a gap to paper over - it is a configuration this service cannot
        // act on, and building a request against an empty address would throw on the donation
        // path. Declining here sends the caller to the deployment's own credentials instead.
        if (string.IsNullOrWhiteSpace(baseUrl)
            && string.Equals(configuration.Provider, "HostedCheckout", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new GatewayCredential(baseUrl, composed, webhookSecret);
    }

    /// <summary>
    /// Where the provider's API lives.
    ///
    /// THE CONFIGURATION SCREEN DOES NOT ASK FOR THIS, AND SHOULD NOT. A base URL is a property
    /// of the provider, not of the merchant; a form that let somebody type one would let somebody
    /// type a URL that is not the provider's, and a merchant credential posted to an attacker's
    /// host is the worst outcome this whole feature can produce.
    ///
    /// So it comes from the deployment's own configuration -
    /// <c>PaymentGateways:{provider}:BaseUrl</c> - and otherwise from the adapter's built-in
    /// default. That is the ordinary case: every adapter already knows its provider's address,
    /// and RazorpayGateway substitutes api.razorpay.com for a blank one.
    /// </summary>
    private string BaseUrlFor(TenantGatewayConfiguration tenantConfiguration)
    {
        var configured =
            deploymentConfiguration[$"PaymentGateways:{tenantConfiguration.Provider}:BaseUrl"]
            ?? deploymentConfiguration["PaymentGateways:Default:BaseUrl"];

        return string.IsNullOrWhiteSpace(configured) ? string.Empty : configured;
    }
}
