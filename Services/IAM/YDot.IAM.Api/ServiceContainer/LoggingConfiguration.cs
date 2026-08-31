using Serilog;
using Serilog.Events;
using YDot.IAM.Api.Middleware;
using YDot.IAM.Application.Common.Abstractions.Security;

namespace YDot.IAM.Api.ServiceContainer;

/// <summary>
/// Serilog.
///
/// The sinks come from the "Serilog" section of appsettings.json rather than from code, so
/// adding Seq or a file in one environment does not mean a rebuild.
///
/// WHAT IS ENRICHED ONTO EVERY LINE — and why it matters more here than in a single-tenant
/// service: the Organisation, the user and the correlation id. Without the Organisation on the
/// line, a log search for "who deleted that role" returns rows from every customer at once and
/// no way to tell them apart.
///
/// WHAT IS NEVER LOGGED: request and response bodies. They carry passwords, tokens, TOTP secrets
/// and recovery codes, and a log file is exactly the wrong place for them. The audit trail
/// records what changed, with the sensitive fields redacted at the point of writing.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Installs Serilog as the host logger.
    ///
    /// Called BEFORE the container is built, so a failure during startup — a bad connection
    /// string, a missing signing key — is still written somewhere a person can read it.
    /// </summary>
    public static IHostBuilder UseIamSerilog(this IHostBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "YDot.IAM")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));
    }

    /// <summary>
    /// The request log line, one per request, with the Organisation and caller attached.
    ///
    /// This replaces the framework's three lines per request. It is registered AFTER
    /// authentication and tenant resolution in Program.cs, which is what lets it see who the
    /// caller turned out to be rather than logging every request as anonymous.
    /// </summary>
    public static IApplicationBuilder UseIamRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            // A 4xx is a client mistake, not a server fault. Logging it at Error makes the
            // error rate meaningless: a hundred failed sign-ins would look like an outage.
            options.GetLevel = (httpContext, _, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : httpContext.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : IsNoise(httpContext.Request.Path)
                            ? LogEventLevel.Verbose
                            : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("Host", httpContext.Request.Host.Value);
                diagnosticContext.Set("Protocol", httpContext.Request.Protocol);
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());

                if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId))
                {
                    diagnosticContext.Set(CorrelationIdMiddleware.HeaderName, correlationId);
                }

                // Resolved from the request scope rather than from the raw token, so the values
                // are the ones the request ACTUALLY ran under — including a SuperAdmin who has
                // selected an Organisation for the session.
                var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();

                if (tenantContext is not null)
                {
                    diagnosticContext.Set("TenantId", tenantContext.TenantId);
                    diagnosticContext.Set("TenantCode", tenantContext.TenantCode);
                    diagnosticContext.Set("TenantScope", tenantContext.Scope.ToString());
                }

                var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();

                if (currentUser is { IsAuthenticated: true })
                {
                    diagnosticContext.Set("UserId", currentUser.UserId);
                    diagnosticContext.Set("Username", currentUser.Username);
                    diagnosticContext.Set("SessionId", currentUser.SessionId);
                }
            };
        });
    }

    /// <summary>
    /// Health probes and the Swagger assets, which would otherwise dominate the log by volume
    /// and tell nobody anything.
    /// </summary>
    private static bool IsNoise(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/favicon.ico", StringComparison.OrdinalIgnoreCase);
}
