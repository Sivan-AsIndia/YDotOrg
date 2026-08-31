using Microsoft.EntityFrameworkCore;
using Serilog;
using YDot.IAM.Api.Middleware;
using YDot.IAM.Api.ServiceContainer;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.ServiceContainer;
using YDot.IAM.Infrastructure.Persistence;
using YDot.IAM.Infrastructure.Persistence.Seed;
using YDot.IAM.Infrastructure.ServiceContainer;
using Microsoft.AspNetCore.HttpOverrides;

// Serilog is started BEFORE the host so that a failure during startup — a bad connection string,
// a missing signing key — is still written somewhere a person can read, rather than disappearing
// into a process that exited before logging existed.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting YDot IAM API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseIamSerilog();

    // ---- Composition ------------------------------------------------------------------------
    //
    // One call per layer, each owning its own registrations. Nothing in Program.cs knows what a
    // repository or a handler is called.
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);

    var app = builder.Build();

    // ---- Database ------------------------------------------------------------------------------
    await app.InitialiseDatabaseAsync();

    // =============================================================================================
    // PIPELINE. The ORDER below is load-bearing, not stylistic.
    // =============================================================================================

    // 0. THE REAL CLIENT ADDRESS, before anything reads it.
    //
    // WHY THIS IS HERE. The API sits behind nginx, so without this every request appears to come
    // from the proxy's address. Two things then go wrong, and the first is severe:
    //
    //   - THE SIGN-IN RATE LIMIT BECOMES GLOBAL. SignInAttemptsPerMinutePerIp is 20; with one
    //     apparent address for the whole platform that is twenty sign-ins per minute for
    //     EVERYBODY, and a room full of people signing in for a demonstration throttle each
    //     other out.
    //   - Every audit row records the proxy as the actor's address, so "where did this come
    //     from" has one answer for every event ever recorded.
    //
    // KnownNetworks and KnownProxies are cleared deliberately. The default trusts only loopback,
    // and nginx reaches this service from the compose bridge network instead - so the header
    // would be ignored exactly where it is needed. That is safe HERE because nothing but the
    // platform's own nginx can reach the container's port: it is not published to the host except
    // through that proxy. If this service is ever exposed directly, name the proxy explicitly
    // rather than clearing the list, or a caller can forge their own address.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 2,
        KnownNetworks = { },
        KnownProxies = { }
    });

    // 1. Correlation first, so even a request rejected at the very edge carries an id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Exceptions second, so it wraps everything after it — including authentication and
    //    tenant resolution — and can still return the standard envelope.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "YDot IAM API v1");
            options.DocumentTitle = "YDot IAM API";

            // Swagger at the root: the service does nothing else on "/" and a developer opening
            // the base URL should land somewhere useful.
            options.RoutePrefix = string.Empty;
            options.DisplayRequestDuration();
        });
    }
    else
    {
        // Only outside development. In development the Angular client talks over plain HTTP and
        // a redirect would turn every call into a CORS preflight failure that looks like a bug
        // in the API.
        app.UseHttpsRedirection();
    }

    // 3. CORS BEFORE authentication. A preflight OPTIONS carries no token; if it reached the
    //    authentication middleware it would be challenged, the browser would never send the real
    //    request, and every call would fail with a message about CORS rather than about auth.
    app.UseCors(YDot.IAM.Api.ServiceContainer.DependencyInjection.CorsPolicyName);

    app.UseAuthentication();

    // 4. Tenant resolution AFTER authentication and BEFORE authorization. This is the single
    //    most important line in the file.
    //
    //    After authentication, because the tenant_id claim on a VALIDATED token is the primary
    //    source and it does not exist until the token has been checked.
    //
    //    Before authorization, because the permission handlers and the EF query filters both
    //    read ITenantContext. Resolving later would leave the first checks running with no
    //    Organisation, and query filters silently matching nothing — or worse, everything.
    app.UseMiddleware<TenantResolutionMiddleware>();

    // 5. The approval gate, between the two. It needs the Organisation status that tenant
    //    resolution has just established, and it must run before authorization because the
    //    refusal has to be identical for every endpoint - a TenantAdmin holds every Tenant
    //    permission from the moment their Organisation is created, so leaving this to the
    //    permission handlers would let an unapproved Organisation work normally.
    app.UseMiddleware<OrganisationApprovalMiddleware>();

    app.UseAuthorization();

    // 6. Request logging last, so the line it writes can name the Organisation and the caller
    //    that the request actually ran as.
    app.UseIamRequestLogging();

    app.MapControllers();

    // A liveness probe that does NOT touch the database: it answers "is the process up", and a
    // database outage should not make the orchestrator kill a healthy process.
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "YDot.IAM",
        timestampUtc = DateTime.UtcNow
    })).AllowAnonymous().ExcludeFromDescription();

    // Readiness DOES touch it, because a service that cannot reach its database cannot serve.
    app.MapGet("/health/ready", async (IamDbContext database, CancellationToken cancellationToken) =>
    {
        var reachable = await database.Database.CanConnectAsync(cancellationToken);

        return reachable
            ? Results.Ok(new { status = "ready", database = "reachable" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }).AllowAnonymous().ExcludeFromDescription();

    await app.RunAsync();

    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "YDot IAM API terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Migration and seeding at startup.
/// </summary>
internal static class DatabaseInitialisation
{
    /// <summary>
    /// Applies pending migrations and runs the seeder, both gated by configuration.
    ///
    /// MIGRATE-ON-STARTUP IS A CONVENIENCE FOR DEVELOPMENT and is switchable off for anywhere
    /// that runs more than one instance — two instances racing to migrate the same database is
    /// a genuine hazard, and there the migration belongs in the deployment step instead.
    ///
    /// The seeder is idempotent, so running it on every boot is safe: it inserts what is
    /// missing and leaves everything else alone.
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
            .CreateLogger("YDot.IAM.Startup");

        try
        {
            if (settings.ApplyMigrationsOnStartup)
            {
                var context = scope.ServiceProvider.GetRequiredService<IamDbContext>();

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
                var seeder = scope.ServiceProvider.GetRequiredService<IamDbSeeder>();
                await seeder.SeedAsync();

                // SECOND, AND THE ORDER IS LOAD-BEARING. The platform master catalogue hangs
                // off the BusinessUnit that IamDbSeeder creates, so running it first would
                // find no root to attach to and skip everything with a warning.
                var masterSeeder = scope.ServiceProvider.GetRequiredService<GlobalMasterSeeder>();
                await masterSeeder.SeedAsync();
            }

            // The document bucket, created if missing and switched to versioned. Deliberately
            // NOT inside the try/catch that rethrows below: this one logs its own failure and
            // returns, because a document store that is down is a reason for uploads to fail
            // with a clear message, not a reason for nobody to be able to sign in.
            var objectStorage = scope.ServiceProvider
                .GetRequiredService<YDot.IAM.Application.Common.Abstractions.Services.IObjectStorage>();

            await objectStorage.EnsureReadyAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Rethrown deliberately. A service that starts against a database it could not
            // migrate will fail later, in scattered ways, at request time — far harder to
            // diagnose than refusing to start.
            logger.LogCritical(exception, "Database initialisation failed");
            throw;
        }
    }
}
