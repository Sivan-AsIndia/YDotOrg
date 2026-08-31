using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Results;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// Shared base for every controller. It turns one Result into the correct HTTP status code and
/// wraps every payload in the same ApiResponse envelope, so success and failure look alike to
/// the UI and no action ever has to write a status code by hand.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>200 OK with the value, or the mapped failure.</summary>
    protected IActionResult FromResult<TValue>(Result<TValue> result, string? successMessage = null) =>
        result.IsSuccess
            ? Ok(ApiResponse<TValue>.Ok(result.Value!, successMessage, CurrentUser.CorrelationId))
            : Failure<TValue>(result.Error!);

    /// <summary>201 Created with the value and a Location header, or the mapped failure.</summary>
    protected IActionResult CreatedFromResult<TValue>(
        Result<TValue> result,
        string routeName,
        object routeValues,
        string? successMessage = null)
    {
        if (result.IsFailure)
        {
            return Failure<TValue>(result.Error!);
        }

        return CreatedAtRoute(
            routeName,
            routeValues,
            ApiResponse<TValue>.Ok(result.Value!, successMessage, CurrentUser.CorrelationId));
    }

    /// <summary>204 No Content on success, or the mapped failure.</summary>
    protected IActionResult NoContentFromResult(Result result)
    {
        if (result.IsFailure)
        {
            var metrics = HttpContext.RequestServices.GetRequiredService<IDonorMetrics>();
            var route = ControllerContext.ActionDescriptor.AttributeRouteInfo?.Template ?? Request.Path.Value ?? "unknown";

            metrics.RecordFailure(result.Error!.Code, result.Error.StatusCode, route);

            return StatusCode(result.Error.StatusCode, ApiResponse.Fail(result.Error, CurrentUser.CorrelationId));
        }

        return NoContent();
    }

    /// <summary>File download with the audit reference in a response header.</summary>
    protected IActionResult FileFromResult(Result<ExportFile> result)
    {
        if (result.IsFailure)
        {
            return Failure<ExportFile>(result.Error!);
        }

        var file = result.Value!;
        Response.Headers.Append("X-Export-Reference", file.Reference);

        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// The single exit for every failure, which is also where ydot_don_failure_count is
    /// incremented. Counting here rather than in each handler means a new endpoint is measured
    /// the moment it is written, with nothing to remember.
    /// </summary>
    private ObjectResult Failure<TValue>(Error error)
    {
        var metrics = HttpContext.RequestServices.GetRequiredService<IDonorMetrics>();
        var route = ControllerContext.ActionDescriptor.AttributeRouteInfo?.Template ?? Request.Path.Value ?? "unknown";

        metrics.RecordFailure(error.Code, error.StatusCode, route);

        return StatusCode(error.StatusCode, ApiResponse<TValue>.Fail(error, CurrentUser.CorrelationId));
    }
}
