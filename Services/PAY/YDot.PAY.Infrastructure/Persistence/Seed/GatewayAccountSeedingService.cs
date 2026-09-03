using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace YDot.PAY.Infrastructure.Persistence.Seed;

/// <summary>
/// Keeps every organisation supplied with a gateway account, in the background.
///
/// WHY IT IS NOT A ONE-SHOT AT STARTUP, which is what it was and what broke. PAY's only
/// <c>depends_on</c> is the database, so on a first <c>docker compose up</c> it starts alongside
/// IAM and reaches for <c>iam_tenants</c> before IAM has created the table:
///
///     WRN The organisation list could not be read... before IAM has created its schema.
///     INF No organisations exist yet, so no gateway accounts were seeded.
///
/// Seeding then never ran again, so a stack brought up on an empty volume - which is precisely
/// what a colleague handed the compose file gets - had no gateway account and refused every
/// donation with PAYMENT_GATEWAY_NOT_CONFIGURED. The SQL script this replaced did not have the
/// problem because it waited, in a loop, for up to two minutes.
///
/// IT ALSO COVERS THE ORGANISATION CREATED LATER. An organisation added an hour after start needs
/// an account too, and asking somebody to restart the payments service to get one is a footnote
/// nobody reads. The query is two columns from one small table; running it on an interval costs
/// nothing worth measuring.
///
/// IT STOPS ON THE ANSWERS THAT CANNOT CHANGE. Disabled, no usable credentials and a live key are
/// all settled until somebody edits the environment and restarts, so the loop ends rather than
/// asking the same question every minute for the life of the process.
/// </summary>
public sealed class GatewayAccountSeedingService(
    IServiceScopeFactory scopeFactory,
    ILogger<GatewayAccountSeedingService> logger) : BackgroundService
{
    /// <summary>
    /// How often to look again while the answer might still change.
    ///
    /// SHORT AT FIRST, THEN RELAXED. The interesting window is the half-minute after a cold start
    /// while IAM seeds its organisations - a donor is not going to arrive in it, but a developer
    /// watching the stack come up will, and a minute of "not configured" reads as broken. Once
    /// the accounts exist the only thing left to catch is a newly created organisation, which
    /// nobody is racing.
    /// </summary>
    private static readonly TimeSpan EagerInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettledInterval = TimeSpan.FromMinutes(2);

    /// <summary>How long to stay eager before settling down.</summary>
    private static readonly TimeSpan EagerWindow = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var reportedNoOrganisations = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            GatewaySeedOutcome outcome;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                var seeder = scope.ServiceProvider.GetRequiredService<PaymentDbSeeder>();

                outcome = await seeder.SeedConfiguredGatewayAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A SEED THAT THROWS MUST NOT TAKE THE SERVICE DOWN. An unhandled exception in a
                // BackgroundService stops the host, and no gateway account is worth refusing to
                // start the payments API over.
                logger.LogWarning(
                    exception, "Gateway account seeding failed. It will be tried again.");

                await DelayAsync(startedAt, stoppingToken);

                continue;
            }

            switch (outcome)
            {
                case GatewaySeedOutcome.Disabled:
                    return;

                case GatewaySeedOutcome.NotConfigured:
                    logger.LogWarning(
                        "Gateway account seeding is enabled, but the provider named by "
                        + "PaymentSettings:SeedGatewayName has no usable credentials. No accounts "
                        + "were created, so donations will be refused until the provider's key and "
                        + "secret are set in the environment and this service is restarted. "
                        + "No key value is logged.");

                    return;

                case GatewaySeedOutcome.LiveKeyRefused:
                    logger.LogWarning(
                        "Gateway account seeding is enabled, but the provider is configured with a "
                        + "LIVE key. Refusing to create payment accounts automatically - configure "
                        + "them deliberately, per organisation.");

                    return;

                case GatewaySeedOutcome.NoOrganisations:
                    // ONCE, NOT EVERY TIME ROUND. This is the ordinary state of a stack that is
                    // still starting, and it resolves itself within seconds.
                    if (!reportedNoOrganisations)
                    {
                        reportedNoOrganisations = true;

                        logger.LogInformation(
                            "No organisations exist yet, so no gateway accounts have been seeded. "
                            + "Watching for them.");
                    }

                    break;

                case GatewaySeedOutcome.Seeded:
                    // The seeder logs the count itself. Reset, so a later organisation that
                    // arrives into an empty platform is reported again.
                    reportedNoOrganisations = false;

                    break;

                case GatewaySeedOutcome.AlreadyConfigured:
                default:
                    break;
            }

            await DelayAsync(startedAt, stoppingToken);
        }
    }

    private static async Task DelayAsync(DateTimeOffset startedAt, CancellationToken stoppingToken)
    {
        var interval = DateTimeOffset.UtcNow - startedAt < EagerWindow
            ? EagerInterval
            : SettledInterval;

        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The loop's own condition ends it.
        }
    }
}
