using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Results;

namespace YDot.PAY.Api.Middleware;

/// <summary>
/// The last line of defence: turns an unhandled exception into the same six-key envelope every
/// endpoint returns.
///
/// WITHOUT THIS, an unhandled exception produces either an empty 500 or - in development - a
/// full stack trace as HTML, and the Angular interceptor that expects the envelope on every
/// response gets neither <c>message</c> nor <c>errorCode</c> to read.
///
/// THE EXCEPTION DETAIL NEVER REACHES THE CLIENT. It is logged with the correlation id, and the
/// caller is given that id and a generic message. A stack trace tells an attacker which ORM,
/// which version and which table names are in play, and tells a legitimate user nothing they
/// can act on.
///
/// WHAT IT REPLACES. The old middleware caught <c>CustomValidationException</c> and turned it
/// into a 400. That exception is gone: validation is FluentValidation running in the pipeline
/// and a refusal is a Result, so a validation failure never unwinds through here any more.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}. Correlation {CorrelationId}.",
            context.Request.Method, context.Request.Path, correlationId);

        // Already writing? Then the headers are sent and there is nothing useful left to do
        // except let it fail - trying to write a new status here throws a second, more
        // confusing exception on top of the first.
        if (context.Response.HasStarted)
        {
            return;
        }

        var error = Describe(exception);

        context.Response.Clear();
        context.Response.StatusCode = error.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(ApiResponse.Fail(error, correlationId), SerializerOptions));
    }

    /// <summary>
    /// Maps an exception to a catalogue error.
    ///
    /// The concurrency case is the one worth handling explicitly. EF throws
    /// <c>DbUpdateConcurrencyException</c> when the version column moved between read and write
    /// - which is the SAME condition the handlers check for and report as a 409 - so letting it
    /// fall through to a 500 would give two different answers for one situation depending on
    /// which of the two detected it first.
    /// </summary>
    private Error Describe(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => Error.Concurrency(),

        OperationCanceledException => new Error(
            "REQUEST_CANCELLED", "The request was cancelled.", 499),

        _ => new Error(
            "INTERNAL_ERROR",
            environment.IsDevelopment()
                ? $"{exception.GetType().Name}: {exception.Message}"
                : "Something went wrong on the server. Please try again.",
            500)
    };
}
