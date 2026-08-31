namespace YDot.IAM.Api.Middleware;

/// <summary>
/// Gives every request one identifier that ties the log lines, the audit rows and the response
/// together.
///
/// IT RUNS FIRST, before anything that might fail, so even a request that is rejected at the
/// very edge still carries an id somebody can quote.
///
/// AN INCOMING ID IS HONOURED, so a correlation started by the Angular client or by a sibling
/// service survives the hop. It is length-capped and stripped of control characters first,
/// because it is echoed into a response header and written into logs - both places where an
/// unbounded caller-supplied string is a poor idea.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaximumLength = 80;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Sanitise(context.Request.Headers[HeaderName].ToString())
                            ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;

        // Written on the response before anything else can start writing, because a header
        // cannot be added once the body has begun.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string? Sanitise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            trimmed = trimmed[..MaximumLength];
        }

        // Control characters in a header value are a response-splitting risk and are never
        // legitimate in a correlation id.
        return trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
