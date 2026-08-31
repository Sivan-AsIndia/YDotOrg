using YDot.IAM.Application.Common.Results;

namespace YDot.IAM.Api.Middleware;

/// <summary>
/// The last line of defence: turns anything unexpected into the same envelope every other
/// failure uses.
///
/// TWO RULES.
///
/// FIRST, THE CALLER NEVER SEES THE EXCEPTION. A stack trace or a database message tells an
/// attacker about the schema, the file layout and the library versions. The caller gets a
/// generic message plus the correlation id; the detail goes to the log, where the two can be
/// matched up by whoever is allowed to read it.
///
/// SECOND, THE ENVELOPE IS THE SAME. A 500 from here has exactly the same six keys as a 400
/// from a validator, so the Angular error interceptor needs no second code path for the case
/// where something went genuinely wrong.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. Not an error, and writing a response to a closed
            // connection would only produce a second, more confusing exception.
            logger.LogInformation(
                "The request to {Path} was cancelled by the caller.", context.Request.Path);
        }
        catch (Exception exception)
        {
            var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}. Correlation {CorrelationId}.",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                // The body is already going out; there is nothing safe to add.
                return;
            }

            context.Response.Clear();

            var error = Error.Dependency(
                "Something went wrong on our side. Quote the reference below if you contact support.");

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error, correlationId));
        }
    }
}
