using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Seed;

/// <summary>
/// Brings the payments schema up to date and seeds the one thing that can be seeded safely.
///
/// THERE IS ALMOST NOTHING TO SEED HERE, and that is the correct answer rather than an omission.
/// Every other module has reference data - countries, channels, lead sources - that is the same
/// for everybody. This module has donations, receipts and refunds, all of which are records of
/// things that actually happened. Seeding a donation would put money in the books that nobody
/// gave.
///
/// WHAT IS SEEDED is a TEST-MODE gateway account for any organisation that has none, so a fresh
/// installation can walk the donation flow end to end without a merchant account. It is created
/// with <c>IsTestMode = true</c> and an unresolvable credential reference, which means the
/// credential resolver returns null and no real payment can ever be attempted through it - the
/// row exists to make the configuration screen populated and the flow reachable, not to take
/// money.
/// </summary>
public sealed class PaymentDbSeeder(PaymentDbContext context, ILogger<PaymentDbSeeder> logger)
{
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
}
