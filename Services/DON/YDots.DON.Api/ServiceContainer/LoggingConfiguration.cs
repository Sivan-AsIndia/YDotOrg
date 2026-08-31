using Serilog;

namespace YDots.DON.Api.ServiceContainer;

/// <summary>
/// Replaces the default logging providers with Serilog. Sinks, levels and file retention all
/// come from the "Serilog" section of appsettings.json so they can be changed without a rebuild.
/// </summary>
public static class LoggingConfiguration
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "YDots.DON")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

        return builder;
    }

    /// <summary>
    /// One summary line per request instead of the two or three ASP.NET Core writes by default,
    /// with the correlation id attached so the file log can be grepped by it.
    /// </summary>
    public static WebApplication UseSerilogRequestLog(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"] as string ?? httpContext.TraceIdentifier);
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());

                if (httpContext.User.Identity?.IsAuthenticated == true)
                {
                    diagnosticContext.Set("UserId", httpContext.User.Identity.Name);
                }
            };

            // Swagger and the health probe would otherwise fill the log with noise.
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null || httpContext.Response.StatusCode >= 500)
                {
                    return Serilog.Events.LogEventLevel.Error;
                }

                return IsNoise(httpContext.Request.Path)
                    ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information;
            };
        });

        return app;
    }

    private static bool IsNoise(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/swagger")
        || path.StartsWithSegments("/openapi");
}
