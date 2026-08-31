using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Results;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Shared base for every controller. It turns one <see cref="Result"/> into the correct HTTP
/// status code and wraps every payload in the same <c>ApiResponse</c> envelope, so success and
/// failure look alike to the client and no action ever writes a status code by hand.
///
/// THE ENVELOPE IS THE CONTRACT. Six keys, always present, on every response from every
/// endpoint. That is what lets the Angular interceptor read <c>err.message</c> and
/// <c>err.errorCode</c> without knowing which endpoint it called.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Resolved per call rather than injected, so a derived controller does not have to thread
    /// it through its own constructor just to satisfy the base.
    /// </summary>
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    private string CorrelationId =>
        HttpContext.Items["CorrelationId"] as string ?? HttpContext.TraceIdentifier;

    /// <summary>200 OK with the value, or the mapped failure.</summary>
    protected IActionResult FromResult<TValue>(Result<TValue> result, string? successMessage = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? Ok(ApiResponse<TValue>.Ok(result.Value!, successMessage, CorrelationId))
            : Failure<TValue>(result.Error!);
    }

    /// <summary>201 Created with the value and a Location header, or the mapped failure.</summary>
    protected IActionResult CreatedFromResult<TValue>(
        Result<TValue> result,
        string routeName,
        object routeValues,
        string? successMessage = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return Failure<TValue>(result.Error!);
        }

        return CreatedAtRoute(
            routeName,
            routeValues,
            ApiResponse<TValue>.Ok(result.Value!, successMessage, CorrelationId));
    }

    /// <summary>204 No Content on success, or the mapped failure.</summary>
    protected IActionResult NoContentFromResult(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return StatusCode(
                result.Error!.StatusCode, ApiResponse.Fail(result.Error, CorrelationId));
        }

        return NoContent();
    }

    /// <summary>
    /// A file download, with the audit reference in a response header.
    ///
    /// The header is what ties a spreadsheet found on somebody desktop months later back to
    /// the audit row that records who exported it and when.
    /// </summary>
    protected IActionResult FileFromResult(Result<ExportFile> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return Failure<ExportFile>(result.Error!);
        }

        var file = result.Value!;
        Response.Headers.Append("X-Export-Reference", file.Reference);

        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// The single exit for every failure.
    ///
    /// One place means the status code always comes from the error catalogue rather than from
    /// whatever the action author happened to think was right, and the correlation id is never
    /// forgotten.
    /// </summary>
    private ObjectResult Failure<TValue>(Error error) =>
        StatusCode(error.StatusCode, ApiResponse<TValue>.Fail(error, CorrelationId));
}
