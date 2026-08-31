using Microsoft.EntityFrameworkCore;
using Serilog;
using YDots.CAM.API.Middleware;
using YDots.CAM.API.ServiceContainer;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Application.ServiceContainer;
using YDots.CAM.Infrastructure.Multitenancy;
using YDots.CAM.Infrastructure.Persistence;
using YDots.CAM.Infrastructure.Persistence.Seed;
using YDots.CAM.Infrastructure.ServiceContainer;
using Microsoft.AspNetCore.HttpOverrides;

// Serilog is started BEFORE the host so that a failure during startup - a bad connection
// string, a missing signing key - is still written somewhere a person can read, rather than
// disappearing into a process that exited before logging existed.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting YDot Campaign API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseCampaignSerilog();

    // ---- Composition ------------------------------------------------------------------------
    //
    // One call per layer, each owning its own registrations. Program.cs no longer names a
    // repository, a DbContext or a handler - it used to name twelve of them between the CORS
    // setup and the middleware.
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
    // KnownNetworks and KnownProxies are cleared deliberately: the default trusts only loopback,
    // and nginx reaches this service across the compose bridge network, so the header would be
    // ignored exactly where it is needed. Safe here because the container's port is not published
    // to the host except through that proxy. If this service is ever exposed directly, name the
    // proxy explicitly rather than clearing the list, or a caller can forge their own address.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 2,
        KnownNetworks = { },
        KnownProxies = { }
    });

    await app.InitialiseDatabaseAsync();

    // =============================================================================================
    // PIPELINE. The ORDER below is load-bearing, not stylistic.
    // =============================================================================================

    // 1. Correlation first, so even a request rejected at the very edge carries an id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Exceptions second, so it wraps everything after it - including authentication and
    //    tenant resolution - and can still return the standard envelope.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "YDot Campaign API v1");
            options.DocumentTitle = "YDot Campaign API";

            // Swagger at the root: the service does nothing else on "/" and a developer opening
            // the base URL should land somewhere useful.
            options.RoutePrefix = string.Empty;
        });
    }

    app.UseSerilogRequestLogging();

    // Fully qualified: the Application and API layers both expose a DependencyInjection
    // class, and both namespaces are in scope here.
    app.UseCors(YDots.CAM.API.ServiceContainer.DependencyInjection.CorsPolicyName);

    // 3. Authentication before authorization, and both before tenant resolution: the tenant
    //    middleware reads claims that only exist once the token has been validated.
    app.UseAuthentication();

    // 4. TENANT RESOLUTION AFTER AUTHENTICATION AND BEFORE THE ENDPOINT. Registered any earlier
    //    it reads an unauthenticated principal and resolves nothing; registered any later the
    //    query filters have already run against an empty context and every list comes back
    //    empty with no error anywhere to explain it.
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
        service = "YDot.CAM",
        utcNow = DateTimeOffset.UtcNow
    })).AllowAnonymous().ExcludeFromDescription();


    await app.RunAsync();

    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "YDot Campaign API terminated unexpectedly");
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
    /// The Seq sink is added only when a URL is configured. It used to be added unconditionally
    /// with <c>WriteTo.Seq(seqUrl!)</c> - a null-forgiving operator on a value read straight
    /// from configuration - so a deployment without a Seq server threw at startup on a line
    /// that had nothing to do with the service's actual work.
    /// </summary>
    internal static IHostBuilder UseCampaignSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", "CAM")
                .WriteTo.Console();

            var seqUrl = context.Configuration["Seq:ServerUrl"];

            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                configuration.WriteTo.Seq(seqUrl);
            }
        });

    /// <summary>
    /// Applies migrations and seeds the reference data.
    ///
    /// A FAILURE HERE IS RETHROWN, deliberately. A service that starts against a database it
    /// could not migrate will fail later, in scattered ways, at request time - far harder to
    /// diagnose than refusing to start.
    /// </summary>
    internal static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        var settings = app.Configuration
                           .GetSection(DatabaseSettings.SectionName)
                           .Get<DatabaseSettings>()
                       ?? new DatabaseSettings();

        if (!settings.ApplyMigrationsOnStartup && !settings.SeedOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("YDots.CAM.Startup");

        try
        {
            if (settings.ApplyMigrationsOnStartup)
            {
                var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();

                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

                if (pending.Count > 0)
                {
                    logger.LogInformation(
                        "Applying {Count} pending migration(s): {Migrations}",
                        pending.Count, string.Join(", ", pending));

                    await context.Database.MigrateAsync();
                }
                else
                {
                    logger.LogInformation("Database schema is up to date");
                }
            }

            if (settings.SeedOnStartup)
            {
                var seeder = scope.ServiceProvider.GetRequiredService<CampaignDbSeeder>();
                await seeder.SeedAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database initialisation failed");
            throw;
        }
    }
}
