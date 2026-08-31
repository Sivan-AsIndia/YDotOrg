using Serilog.Context;

namespace YDots.DON.Api.Middleware;

/// <summary>
/// Gives every request a stable correlation id. The same id appears in the log, in the audit
/// row and in the response body, so a support call can be traced end to end.
///
/// If the Angular client sends X-Correlation-Id, it is honoured, which means one browser action
/// can be followed across both IAM and DON under a single reference.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var provided) && !string.IsNullOrWhiteSpace(provided)
            ? provided.ToString()
            : Guid.NewGuid().ToString("N").ToUpperInvariant();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Everything logged further down the pipeline carries the id without having to pass it around.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
