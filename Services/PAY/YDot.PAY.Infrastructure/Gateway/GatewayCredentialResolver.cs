using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Turns the credential REFERENCES held on a gateway account row into the actual secrets.
///
/// THIS INDIRECTION IS THE POINT OF THE WHOLE DESIGN. <c>pay_gateway_accounts</c> stores
/// <c>api_key_reference</c> and <c>webhook_secret_reference</c> - names like
/// "Gateways:Razorpay:Acme" - and never the keys themselves. The keys live wherever the
/// deployment keeps secrets: environment variables, user secrets in development, a mounted file,
/// Key Vault or Secrets Manager through a configuration provider.
///
/// WHAT THAT BUYS. A dump of the payments database - the thing an SQL injection or a stolen
/// backup gets you - contains no credential for any charity's merchant account. Rotating a key
/// is a deployment change, not a database migration. And a developer restoring production data
/// into a test environment cannot accidentally take real money, because the references resolve
/// to nothing there.
///
/// IT IS DELIBERATELY NOT AN ENCRYPTION LAYER. Encrypting the key in the row would put the key
/// and the means to read it in the same database, which protects against nothing that matters.
/// </summary>
public interface IGatewayCredentialResolver
{
    /// <summary>
    /// The credentials for an account, or null when they are not configured.
    ///
    /// NULL IS AN EXPECTED ANSWER, not an exceptional one. An organisation that has created its
    /// gateway account row but whose secrets have not yet been deployed is a normal state during
    /// onboarding, and the callers report it as PAYMENT_GATEWAY_NOT_CONFIGURED - which tells a
    /// donor to contact the charity rather than that something broke.
    /// </summary>
    GatewayCredential? Resolve(PaymentGatewayAccount account);
}

/// <summary>
/// One organisation's resolved gateway credentials.
///
/// It never leaves the infrastructure layer and is never serialised, logged or audited.
/// </summary>
public sealed record GatewayCredential(string BaseUrl, string ApiKey, string? WebhookSecret);

/// <summary>
/// Resolves credentials from the application's configuration, whatever provider supplies it.
///
/// USING <c>IConfiguration</c> RATHER THAN A SPECIFIC VAULT CLIENT is what makes the same code
/// work in every environment: user secrets in development, environment variables in a container,
/// and a Key Vault or Secrets Manager configuration provider in production - all of which present
/// themselves as configuration keys. Binding to one vendor's SDK here would mean a second
/// implementation for local development, and two code paths where the one that is never exercised
/// is the one handling real money.
/// </summary>
public sealed class ConfigurationGatewayCredentialResolver(
    IConfiguration configuration, ILogger<ConfigurationGatewayCredentialResolver> logger)
    : IGatewayCredentialResolver
{
    /// <summary>The configuration section every gateway credential lives under.</summary>
    private const string SectionPrefix = "PaymentGateways";

    public GatewayCredential? Resolve(PaymentGatewayAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.ApiKeyReference))
        {
            logger.LogWarning(
                "Gateway account {AccountId} for merchant {MerchantId} has no API key reference, "
                + "so no payment can be taken through it.",
                account.Id,
                account.MerchantId);

            return null;
        }

        var section = configuration.GetSection($"{SectionPrefix}:{account.ApiKeyReference}");

        var apiKey = section["ApiKey"];
        var baseUrl = section["BaseUrl"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl))
        {
            // The reference names a section that does not exist or is incomplete. Logged WITHOUT
            // the values - the whole point of the indirection is that they never appear anywhere
            // a log is read.
            logger.LogWarning(
                "No usable credentials are configured at {SectionPrefix}:{Reference} for merchant "
                + "{MerchantId}. Payments through this account will be refused until they are "
                + "deployed.",
                SectionPrefix,
                account.ApiKeyReference,
                account.MerchantId);

            return null;
        }

        // The webhook secret may legitimately live under its own reference - some providers issue
        // it separately and rotate it on a different schedule - and falls back to the same
        // section when it does not.
        var webhookSecret = string.IsNullOrWhiteSpace(account.WebhookSecretReference)
            ? section["WebhookSecret"]
            : configuration[$"{SectionPrefix}:{account.WebhookSecretReference}:WebhookSecret"]
              ?? configuration[$"{SectionPrefix}:{account.WebhookSecretReference}"];

        return new GatewayCredential(baseUrl, apiKey, webhookSecret);
    }
}
