using Serilog;
using YDot.PAY.Api.Middleware;
using YDot.PAY.Api.ServiceContainer;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.ServiceContainer;
using YDot.PAY.Infrastructure.Multitenancy;
using YDot.PAY.Infrastructure.Persistence.Seed;
using YDot.PAY.Infrastructure.ServiceContainer;
using Microsoft.AspNetCore.HttpOverrides;

// Serilog is started BEFORE the host so that a failure during startup - a bad connection string,
// a missing signing key - is still written somewhere a person can read, rather than disappearing
// into a process that exited before logging existed.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting YDot Donations and Payments API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UsePaymentSerilog();

    // ---- Composition ------------------------------------------------------------------------
    //
    // One call per layer, each owning its own registrations. Program.cs names no repository, no
    // DbContext and no handler.
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);

    var app = builder.Build();


    // THE REAL CLIENT ADDRESS, before anything reads it.
    //
    // This service sits behind nginx, so without this every request appears to come from the
    // proxy. Every audit row this service writes records the caller's address, so the whole trail
    // would say "the proxy" and "where did this come from?" would have one answer for every event
    // ever recorded.
    //
    // WHICH PROXIES ARE TRUSTED is the part that has to be right, and it used to be written as
    // `KnownNetworks = { }` in an object initialiser - which adds nothing and leaves the
    // loopback-only defaults standing, so the header nginx sets was silently ignored and every
    // row recorded the proxy's own container address. See ForwardedHeadersConfiguration for the
    // full account and for the environment variable that narrows the trusted set.
    app.UseForwardedHeaders(ForwardedHeadersConfiguration.Build(builder.Configuration));

    await app.InitialiseDatabaseAsync();

    // =============================================================================================
    // PIPELINE. The ORDER below is load-bearing, not stylistic.
    // =============================================================================================

    // 1. Correlation first, so even a request rejected at the very edge carries an id. It matters
    //    more in this service than the others: the id is quoted back to the DONOR on the payment
    //    verification screen, so it is the string a support conversation starts from.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Exceptions second, so it wraps everything after it - including authentication and tenant
    //    resolution - and can still return the standard envelope.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "YDot Donations and Payments API v1");
            options.DocumentTitle = "YDot Donations and Payments API";

            // Swagger at the root: the service does nothing else on "/" and a developer opening
            // the base URL should land somewhere useful.
            options.RoutePrefix = string.Empty;
        });
    }

    app.UseSerilogRequestLogging();

    // Fully qualified: the Application and API layers both expose a DependencyInjection class,
    // and both namespaces are in scope here.
    app.UseCors(YDot.PAY.Api.ServiceContainer.DependencyInjection.CorsPolicyName);

    // 3. Authentication before tenant resolution: the tenant middleware reads claims that only
    //    exist once the token has been validated.
    app.UseAuthentication();

    // 4. TENANT RESOLUTION AFTER AUTHENTICATION AND BEFORE AUTHORIZATION. Registered any earlier
    //    it reads an unauthenticated principal and resolves nothing; registered any later the
    //    query filters have already run against an empty context and every list comes back empty
    //    with no error anywhere to explain it.
    //
    //    IN THIS SERVICE IT ALSO RESOLVES THE PUBLIC PATHS, from the donation reference in the
    //    route, which is why it must run for unauthenticated requests too rather than being
    //    skipped for them.
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.UseAuthorization();

    app.MapControllers();

    /*
     * Liveness, anonymous.
     *
     * THERE WAS NO /health HERE AT ALL, while the compose healthcheck polled it every fifteen
     * seconds. Every probe fell through to the controller pipeline and answered 401, so the
     * container never became healthy - it sat in "health: starting" until Docker gave up and
     * marked it unhealthy, on a service that was working perfectly.
     *
     * IT IS DELIBERATELY SHALLOW. It says this process is up and answering; it does not touch the
     * database. A liveness probe that fails when the database is briefly unavailable restarts a
     * healthy application in the middle of an outage, which is the worst possible moment.
     */
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "Healthy",
        service = "YDot.PAY",
        utcNow = DateTimeOffset.UtcNow
    })).AllowAnonymous().ExcludeFromDescription();


    await app.RunAsync();

    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "YDot Donations and Payments API terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Startup helpers, kept out of the top-level statements above.</summary>
internal static class StartupExtensions
{
    /// <summary>
    /// Serilog, reading its sinks from configuration.
    ///
    /// The Seq sink is added only when a URL is configured, so a deployment without a Seq server
    /// does not throw at startup on a line that has nothing to do with the service's actual work.
    /// </summary>
    internal static IHostBuilder UsePaymentSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", "PAY")
                .WriteTo.Console();

            var seqUrl = context.Configuration["Seq:ServerUrl"];

            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                configuration.WriteTo.Seq(seqUrl);
            }
        });

    /// <summary>
    /// Applies migrations.
    ///
    /// A FAILURE HERE IS RETHROWN, deliberately. A service that starts against a database it
    /// could not migrate will fail later, in scattered ways, at request time - far harder to
    /// diagnose than refusing to start. On a service that records money the difference is between
    /// finding out now and finding out from a donor.
    ///
    /// THERE IS NO REFERENCE DATA TO SEED HERE, unlike the other three services. Everything in
    /// these tables is a record of something that actually happened, and seeding a donation would
    /// put money in the books that nobody gave. The seeder's one job - a disabled test gateway
    /// account - is per-organisation and is invoked when an organisation is onboarded, not on
    /// every start.
    /// </summary>
    internal static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        var settings = app.Configuration
                           .GetSection(DatabaseSettings.SectionName)
                           .Get<DatabaseSettings>()
                       ?? new DatabaseSettings();

        if (!settings.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("YDot.PAY.Startup");

        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<PaymentDbSeeder>();

            await seeder.MigrateAsync();

            // GATEWAY ACCOUNTS ARE NOT SEEDED HERE. They were, and it did not work: this runs
            // while IAM is still starting, so iam_tenants does not exist yet and the seed found
            // nothing to do - permanently, because it only ran once. GatewayAccountSeedingService
            // keeps looking until the organisations appear.
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Payments database initialisation failed");
            throw;
        }
    }
}
