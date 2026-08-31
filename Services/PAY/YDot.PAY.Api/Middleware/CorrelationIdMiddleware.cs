namespace YDot.PAY.Api.Middleware;

/// <summary>
/// Gives every request one identifier that ties the HTTP call, the log lines and the audit rows
/// together.
///
/// IT RUNS FIRST IN THE PIPELINE, so even a request rejected at the very edge - a malformed
/// token, a failed authorization - still carries an id somebody can quote. An id assigned later
/// would be missing from exactly the responses people ask about.
///
/// AN INBOUND HEADER IS HONOURED, so a correlation id set by the Angular client or by a gateway
/// survives into this service's logs and the trail can be followed across the whole hop. It is
/// LENGTH-CAPPED, because it ends up in a bounded audit column and in log lines - an unbounded
/// caller-supplied string in both places is a log-flooding vector.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaximumLength = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();

        var correlationId = string.IsNullOrWhiteSpace(supplied)
            ? context.TraceIdentifier
            : supplied.Length > MaximumLength ? supplied[..MaximumLength] : supplied;

        context.Items["CorrelationId"] = correlationId;

        // Echoed back so the caller can quote it, and attached BEFORE the response starts - once
        // the body is being written the headers are already sent and this would throw.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
