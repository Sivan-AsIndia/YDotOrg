using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Results;

namespace YDots.DON.Api.Middleware;

/// <summary>
/// The last safety net. An unexpected exception never leaks a stack trace to the caller: it
/// becomes the same ApiResponse envelope with a stable error code and the correlation id.
///
/// The two DbUpdate cases are here rather than in a handler because they can only be detected
/// at SaveChanges. A concurrency conflict is the "somebody else saved first" case from section
/// 11, and a duplicate key is the natural-key collision the unique indexes enforce.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Concurrency conflict on {Path}", context.Request.Path);
            await WriteAsync(context, Error.Concurrency());
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Database update failed on {Path}", context.Request.Path);

            var error = exception.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true
                ? Error.Duplicate("A record with the same business key already exists.")
                : Error.Dependency("The database rejected the change. Retry using the correlation reference below.");

            await WriteAsync(context, error);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request cancelled by the caller on {Path}", context.Request.Path);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);
            await WriteAsync(context, Error.Dependency(
                "The request could not be completed. Quote the correlation reference to support."));
        }
    }

    private static async Task WriteAsync(HttpContext context, Error error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = error.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(ApiResponse.Fail(error, correlationId), SerializerOptions));
    }
}
