using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Seed;

/// <summary>
/// What one attempt at seeding gateway accounts actually did.
///
/// IT IS RETURNED RATHER THAN LOGGED, because the caller is a retry loop and only it knows
/// whether a given answer is worth saying out loud. "No organisations yet" is the normal state of
/// a stack thirty seconds into its first start and would be noise every thirty seconds after
/// that; it is worth one line the first time and nothing thereafter.
///
/// The three terminal outcomes - <see cref="Disabled"/>, <see cref="NotConfigured"/> and
/// <see cref="LiveKeyRefused"/> - are the ones that will never change without a restart, so the
/// loop stops on them instead of asking again for ever.
/// </summary>
public enum GatewaySeedOutcome
{
    /// <summary>Seeding is switched off. Nothing was looked at.</summary>
    Disabled,

    /// <summary>No usable credentials for the named provider. A restart is needed to fix it.</summary>
    NotConfigured,

    /// <summary>A live key. Refused deliberately - see the seeder.</summary>
    LiveKeyRefused,

    /// <summary>No organisations exist yet, or IAM has not created its schema. Try again shortly.</summary>
    NoOrganisations,

    /// <summary>Every organisation already had an account. Nothing to do.</summary>
    AlreadyConfigured,

    /// <summary>Accounts were created.</summary>
    Seeded
}

/// <summary>
/// Brings the payments schema up to date and seeds the one thing that can be seeded safely.
///
/// THERE IS ALMOST NOTHING TO SEED HERE, and that is the correct answer rather than an omission.
/// Every other module has reference data - countries, channels, lead sources - that is the same
/// for everybody. This module has donations, receipts and refunds, all of which are records of
/// things that actually happened. Seeding a donation would put money in the books that nobody
/// gave.
///
/// WHAT IS SEEDED is a gateway account, and there are two of them for two different purposes:
///
///   <see cref="SeedTestGatewayAccountAsync"/>          one organisation, DISABLED, no credentials
///   <see cref="SeedConfiguredGatewayAccountsAsync"/>   every organisation, ACTIVE, from config
///
/// The first exists so a configuration screen is populated before a merchant account is set up.
/// The second exists so a development stack takes a real test payment without anybody running a
/// SQL script by hand - it is off unless asked for, and refuses live keys outright.
/// </summary>
public sealed class PaymentDbSeeder(
    PaymentDbContext context,
    IConfiguration configuration,
    IOptions<PaymentSettings> paymentOptions,
    ILogger<PaymentDbSeeder> logger)
{
    private readonly PaymentSettings _settings = paymentOptions.Value;

    /// <summary>
    /// Applies pending migrations.
    ///
    /// SEPARATE FROM SEEDING because the two have different risk profiles. Migrating is
    /// idempotent and safe to run on every start; seeding writes rows, and an installation may
    /// legitimately want the schema without the sample account.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);

        var pendingList = pending.ToList();

        if (pendingList.Count == 0)
        {
            logger.LogInformation("The payments schema is up to date.");

            return;
        }

        logger.LogInformation(
            "Applying {Count} pending payments migration(s): {Migrations}",
            pendingList.Count,
            string.Join(", ", pendingList));

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds a test-mode gateway account for the given organisation, if it has none at all.
    ///
    /// IT IS SCOPED TO ONE ORGANISATION AND TAKES THE ID EXPLICITLY, rather than sweeping every
    /// tenant. A seeder that walked the tenant table would create a gateway account for every
    /// charity on the platform including live ones, and a stray row in a payments configuration
    /// screen is exactly the kind of thing somebody activates without reading.
    ///
    /// IT DOES NOTHING IF ANY ACCOUNT ALREADY EXISTS - including a live one - so it can never
    /// interfere with a configured organisation.
    /// </summary>
    public async Task SeedTestGatewayAccountAsync(
        Guid tenantId, Guid businessUnitId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        var alreadyConfigured = await context.GatewayAccounts
            .IgnoreQueryFilters()
            .AnyAsync(account => account.TenantId == tenantId, cancellationToken);

        if (alreadyConfigured)
        {
            return;
        }

        context.GatewayAccounts.Add(new PaymentGatewayAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            GatewayName = "HostedCheckout",

            // Recognisable at a glance as not a real merchant id.
            MerchantId = $"TEST-{tenantId.ToString("N")[..12].ToUpperInvariant()}",

            // A reference that resolves to nothing, deliberately. The credential resolver returns
            // null, every payment attempt is refused with PAYMENT_GATEWAY_NOT_CONFIGURED, and no
            // real charge can be made through this row however it is later edited.
            ApiKeyReference = "Sandbox:NotConfigured",
            WebhookSecretReference = null,

            IsTestMode = true,

            // INACTIVE. An operator must deliberately turn it on, which is one more step than
            // "it worked in development and nobody noticed it went live".
            IsActive = false,

            SettlementCurrencyCode = "INR",
            ReturnUrl = null,
            WebhookUrl = null,
            PaymentLinkValidityMinutes = 60,
            EnabledMethods = "card,netbanking,upi,wallet",
            Notes =
                "Created automatically so the donation flow is reachable before a merchant "
                + "account is configured. It holds no credentials and cannot take a real payment.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.Empty,
            Version = 1
        });

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded a disabled test-mode gateway account for organisation {TenantId}.", tenantId);
    }

    /// <summary>
    /// Gives every organisation an ACTIVE gateway account pointed at the configured provider.
    ///
    /// THIS IS WHAT REMOVED THE SQL FILE. <c>docker/sql/razorpay-test-gateway.sql</c>, run by a
    /// one-shot compose container against a bind-mounted folder, did exactly this - and it meant
    /// the payment module could not be started from a compose file and a .env alone. Somebody
    /// handed those two and nothing else got a stack where every donation was refused with
    /// PAYMENT_GATEWAY_NOT_CONFIGURED, and the seed container's entrypoint ended in
    /// <c>|| echo skipped</c>, so it exited 0 with nothing to show for it.
    ///
    /// THE ROW STILL HOLDS NO CREDENTIAL, which is the property that makes this safe to write from
    /// a seeder at all. It stores the NAME of the configuration section - "Razorpay" - and the
    /// credential resolver reads the key from <c>PaymentGateways:Razorpay:ApiKey</c> at the moment
    /// of use. Nothing secret reaches the database, and the row is as safe to dump or restore as
    /// it was before.
    ///
    /// IT REFUSES A LIVE KEY, unconditionally and whatever the setting says. Creating an active
    /// payment configuration for every organisation on a platform is defensible when the worst
    /// case is a declined test charge; it is not defensible when the keys move real money. The key
    /// prefix is the only reliable signal - <c>IsTestMode</c> on the row means "sandbox row", not
    /// "test key", and is deliberately false here because
    /// <c>GetActiveForTenantAsync</c> filters on <c>IsActive &amp;&amp; !IsTestMode</c>, so a row
    /// marked test mode is never selected and would take no test payment either.
    ///
    /// IT IS IDEMPOTENT and matches the unique index on (tenant_id, gateway_name, is_test_mode),
    /// so a restart is a no-op and an organisation that already has an account is never touched -
    /// including one somebody configured by hand.
    /// </summary>
    public async Task<GatewaySeedOutcome> SeedConfiguredGatewayAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_settings.SeedGatewayAccountsFromConfiguration)
        {
            return GatewaySeedOutcome.Disabled;
        }

        var gatewayName = _settings.SeedGatewayName?.Trim();

        if (string.IsNullOrWhiteSpace(gatewayName))
        {
            return GatewaySeedOutcome.NotConfigured;
        }

        var apiKey = configuration[$"PaymentGateways:{gatewayName}:ApiKey"];
        var baseUrl = configuration[$"PaymentGateways:{gatewayName}:BaseUrl"];

        // ':' IS WHAT AN UNFILLED .env PRODUCES, not a credential. docker-compose builds the key
        // as "${RAZORPAY_KEY_ID:-}:${RAZORPAY_KEY_SECRET:-}", so a .env copied from .env.example -
        // where both are deliberately empty - yields the single character ':'. That is neither
        // null nor whitespace, so it passes every ordinary emptiness check, reaches the provider
        // and comes back 401. Seeding an account against it would only move the confusion later.
        var keyIsUsable = !string.IsNullOrWhiteSpace(apiKey)
                          && apiKey.Trim(':').Length > 0
                          && !string.IsNullOrWhiteSpace(baseUrl);

        if (!keyIsUsable)
        {
            return GatewaySeedOutcome.NotConfigured;
        }

        if (apiKey.Contains("_live_", StringComparison.OrdinalIgnoreCase))
        {
            return GatewaySeedOutcome.LiveKeyRefused;
        }

        // The organisations come from IAM's table in the shared database, the same seam DON's
        // campaign projection reads across. A seeder has no session and no tenant context, so
        // there is nothing else to enumerate them by.
        var tenants = await ReadTenantsAsync(cancellationToken);

        if (tenants.Count == 0)
        {
            return GatewaySeedOutcome.NoOrganisations;
        }

        var configured = await context.GatewayAccounts
            .IgnoreQueryFilters()
            .Where(account => account.GatewayName == gatewayName && !account.IsTestMode)
            .Select(account => account.TenantId)
            .ToListAsync(cancellationToken);

        var existing = configured.ToHashSet();
        var added = 0;

        foreach (var tenant in tenants.Where(tenant => !existing.Contains(tenant.Id)))
        {
            context.GatewayAccounts.Add(new PaymentGatewayAccount
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                BusinessUnitId = tenant.BusinessUnitId,
                GatewayName = gatewayName,

                // Unique per (gateway_name, merchant_id) as the schema requires, and recognisable
                // at a glance as a row nobody typed.
                MerchantId =
                    $"{gatewayName.ToUpperInvariant()}-{tenant.Id.ToString("N")[..12].ToUpperInvariant()}",

                // The SECTION NAME, not a secret. See the summary above.
                ApiKeyReference = gatewayName,

                // Null means "the webhook secret lives in the same section as the API key", which
                // is what compose supplies. A separate reference is only for providers that rotate
                // the two apart.
                WebhookSecretReference = null,

                // NOT a sandbox row - see the summary.
                IsTestMode = false,
                IsActive = true,

                SettlementCurrencyCode = _settings.SeedGatewaySettlementCurrency,

                // No per-account return URL: the gateway falls back to ClientAppSettings' BaseUrl
                // plus PaymentResultPath, which is where the donor is sent back to on a dev box.
                ReturnUrl = null,

                // No webhook URL. A provider cannot call a machine that is not on the internet,
                // and nothing is lost locally: the donor's browser returns to the result page,
                // which asks us to verify, and verification is a PULL - our server calls the
                // provider, so the outcome is confirmed with no inbound connectivity.
                WebhookUrl = null,

                PaymentLinkValidityMinutes = _settings.DefaultPaymentLinkValidityMinutes,
                EnabledMethods = "card,netbanking,upi,wallet",
                Notes =
                    "Created automatically from configuration "
                    + "(PaymentSettings:SeedGatewayAccountsFromConfiguration). Holds no "
                    + $"credentials; the keys come from PaymentGateways:{gatewayName} in the "
                    + "environment. Test keys only.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = Guid.Empty,
                Version = 1
            });

            added++;
        }

        if (added == 0)
        {
            return GatewaySeedOutcome.AlreadyConfigured;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Count} {Gateway} gateway account(s) from configuration.", added, gatewayName);

        return GatewaySeedOutcome.Seeded;
    }

    /// <summary>
    /// The organisations, read from IAM's table in the shared database.
    ///
    /// RAW SQL BECAUSE THE ENTITY BELONGS TO ANOTHER MODULE. PAY has no <c>Tenant</c> type and
    /// should not acquire one for a development seed; the two columns it needs are the id and the
    /// business unit that owns it.
    ///
    /// IT NEVER THROWS. On a first start PAY may reach its own migration before IAM has created
    /// its tables at all, and a missing table must not stop the payments service from booting -
    /// the accounts are seeded on a later start instead.
    /// </summary>
    private async Task<List<(Guid Id, Guid BusinessUnitId)>> ReadTenantsAsync(
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid)>();

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (opened)
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, business_unit_id FROM iam_tenants";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }
        catch (Exception exception)
        {
            // DEBUG, NOT WARNING. On a first start PAY reaches this before IAM has created
            // iam_tenants at all, and the caller retries - so at Warning this printed an alarming
            // line about a condition that resolves itself seconds later, on every healthy boot.
            logger.LogDebug(
                exception,
                "The organisation list could not be read yet. Expected before IAM has created "
                + "its schema; the seed is retried.");

            return [];
        }
        finally
        {
            if (opened && connection.State == System.Data.ConnectionState.Open)
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        return rows;
    }
}
