using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using YDots.DON.Api.Middleware;
using YDots.DON.Api.ServiceContainer;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.ServiceContainer;
using YDots.DON.Infrastructure.Multitenancy;
using YDots.DON.Infrastructure.Persistence;
using YDots.DON.Infrastructure.Persistence.Seed;
using YDots.DON.Infrastructure.ServiceContainer;
using Microsoft.AspNetCore.HttpOverrides;

// Console-only logger for the startup window. A bad configuration section or a failed
// registration happens before the real logger exists, and would otherwise vanish silently.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddSerilogLogging();

    // ---- Composition root: API composes Application and Infrastructure ------------------------
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

    // ---- Migrate and seed on startup ------------------------------------------------------------
    using (var scope = app.Services.CreateScope())
    {
        var provider = scope.ServiceProvider;
        var databaseSettings = provider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
        var logger = provider.GetRequiredService<ILogger<Program>>();

        try
        {
            var context = provider.GetRequiredService<DonDbContext>();

            if (databaseSettings.ApplyMigrationsOnStartup)
            {
                logger.LogInformation("Applying pending EF Core migrations...");
                await context.Database.MigrateAsync();
            }

            if (databaseSettings.SeedOnStartup)
            {
                logger.LogInformation("Seeding Donors reference data...");
                await provider.GetRequiredService<DonDbSeeder>().SeedAsync();
            }
        }
        catch (Exception exception)
        {
            // The API still starts, so the failure is visible in the log rather than as a silent crash.
            logger.LogError(exception, "Database migration or seeding failed. Check the connection string in DatabaseSettings.");
        }
    }

    // ---- Request pipeline. The order below is the order every request travels through. -------------
    app.UseMiddleware<CorrelationIdMiddleware>();      // 1. Give the request a correlation id
    app.UseMiddleware<RequestMetricsMiddleware>();     // 2. Time it and open the trace span
    app.UseSerilogRequestLog();                        // 3. One summary line per request
    app.UseMiddleware<ExceptionHandlingMiddleware>();  // 4. Catch anything unexpected

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "YDot Donors API v1");
        options.DocumentTitle = "YDot Donors and Leads";
        options.DisplayRequestDuration();
    });

    app.UseHttpsRedirection();
    app.UseCors("YDotClients");

    app.UseAuthentication();   // 5. Read and validate the JWT that IAM signed (who are you?)

    // 6. Resolve the Organisation from the validated token.
    //
    // AFTER UseAuthentication AND BEFORE THE ENDPOINT, and both halves matter. Any earlier it
    // reads an unauthenticated principal and resolves nothing; any later the global query
    // filters have already run against an empty context and every list comes back empty with no
    // error anywhere to explain it.
    app.UseMiddleware<OrganisationResolutionMiddleware>();

    app.UseAuthorization();    // 7. Check the permission claim the endpoint asks for (what may you do?)

    app.MapControllers();      // 8. Route to the controller action

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "YDot.DON", utcNow = DateTimeOffset.UtcNow }))
        .AllowAnonymous()
        .WithName("HealthCheck");

    Log.Information("YDot Donors API starting in {Environment}.", app.Environment.EnvironmentName);

    await app.RunAsync();
}
// "dotnet ef" builds the host and then aborts it on purpose. That is not a crash.
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "YDot Donors API terminated unexpectedly.");
}
finally
{
    // The Async sink buffers, so anything still queued has to be drained before the process ends.
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so integration tooling can reference the entry point in the future.</summary>
public partial class Program;
