using System.Diagnostics;

namespace YDot.IAM.Api.Middleware;

/// <summary>
/// One summary line per request: method, path, status, duration, Organisation and caller.
///
/// THE PATH IS LOGGED WITHOUT ITS QUERY STRING, deliberately. Reset and invitation links carry
/// their token as a query parameter, and a token in a log file is a live token for anybody who
/// can read logs. The route template gives all the diagnostic value without the secret.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var status = context.Response.StatusCode;

            // A failure is worth a warning; a slow success is worth noticing too.
            var level = status >= 500
                ? LogLevel.Error
                : status >= 400
                    ? LogLevel.Warning
                    : stopwatch.ElapsedMilliseconds > 2000
                        ? LogLevel.Warning
                        : LogLevel.Information;

            logger.Log(
                level,
                "{Method} {Path} responded {Status} in {Elapsed} ms. Organisation {TenantCode}. User {User}.",
                context.Request.Method,
                context.Request.Path.Value,
                status,
                stopwatch.ElapsedMilliseconds,
                context.Items["TenantCode"] as string ?? "(none)",
                context.User.Identity?.Name ?? "(anonymous)");
        }
    }
}
