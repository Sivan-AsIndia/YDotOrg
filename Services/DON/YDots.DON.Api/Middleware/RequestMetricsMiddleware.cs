using System.Diagnostics;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Infrastructure.Observability;

namespace YDots.DON.Api.Middleware;

/// <summary>
/// Records ydot_don_request_duration and opens the dependency-trace span for every request.
///
/// The route template is used as the tag rather than the raw path, so a thousand calls to
/// /api/v1/donors/{id} produce one time series instead of a thousand — and no donor identifier
/// ever ends up in a metric label, which would be a quiet privacy leak.
/// </summary>
public sealed class RequestMetricsMiddleware(RequestDelegate next, IDonorMetrics metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        using var activity = DonorMetrics.ActivitySource.StartActivity(
            $"{context.Request.Method} {context.Request.Path}", ActivityKind.Server);

        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("http.method", context.Request.Method);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            // The route template is only known after routing has run, which is why this is read
            // at the end rather than at the start.
            var route = context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
                ? "/" + endpoint.RoutePattern.RawText?.TrimStart('/')
                : context.Request.Path.Value ?? "unknown";

            metrics.RecordRequestDuration(route, context.Request.Method, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetTag("http.status_code", context.Response.StatusCode);
            activity?.SetStatus(context.Response.StatusCode >= 500 ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }
    }
}
