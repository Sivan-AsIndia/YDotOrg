using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.DuplicateReview.Commands.DecideDuplicate;
using YDots.DON.Application.Features.DuplicateReview.DTOs;
using YDots.DON.Application.Features.DuplicateReview.Queries.GetDuplicateReview;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-004 Duplicate review. Compare candidates and decide link, merge or keep separate.
/// Route from the developer contract: /api/v1/donors/duplicate-review.
/// </summary>
[Route("api/v1/donors/duplicate-review")]
[Authorize]
public sealed class DuplicateReviewController : ApiControllerBase
{
    /// <summary>GET the review queue plus its filter options.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.DuplicateReviewView)]
    [ProducesResponseType(typeof(ApiResponse<DuplicateReviewListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] DuplicateReviewSearchFilter filter,
        [FromServices] DuplicateReviewQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetDuplicateReviewListQuery(filter), cancellationToken));

    /// <summary>GET one review: both candidates, the evidence and the merge preview.</summary>
    [HttpGet("{id:guid}", Name = "GetDuplicateReviewById")]
    [HasPermission(PermissionCodes.DuplicateReviewView)]
    [ProducesResponseType(typeof(ApiResponse<DuplicateReviewDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] DuplicateReviewQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetDuplicateReviewDetailQuery(id), cancellationToken));

    /// <summary>POST a new review for two candidate donors.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.DuplicateReviewView)]
    [ProducesResponseType(typeof(ApiResponse<DuplicateReviewDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDuplicateReviewRequest request,
        [FromServices] DuplicateReviewCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateDuplicateReviewCommand(request), cancellationToken);

        return CreatedFromResult(result, "GetDuplicateReviewById", new { id = result.Value?.Id ?? Guid.Empty },
            "The duplicate review was raised.");
    }

    /// <summary>
    /// POST the decision: Merge, Link or KeepSeparate. A merge repoints the absorbed record's
    /// children onto the survivor and leaves the absorbed record in place as Merged.
    /// </summary>
    [HttpPost("{id:guid}/merge")]
    [HasPermission(PermissionCodes.DuplicateReviewMerge)]
    [ProducesResponseType(typeof(ApiResponse<DuplicateReviewDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Merge(
        Guid id,
        [FromBody] MergeDecisionRequest request,
        [FromServices] DuplicateReviewCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new MergeDuplicateCommand(id, request), cancellationToken),
            "The duplicate decision was recorded.");

    /// <summary>POST reject candidate. Danger action: the pair is recorded as not a match.</summary>
    [HttpPost("{id:guid}/reject-candidate")]
    [HasPermission(PermissionCodes.DuplicateReviewRejectCandidate)]
    [ProducesResponseType(typeof(ApiResponse<DuplicateReviewDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectCandidate(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] DuplicateReviewCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new RejectDuplicateCandidateCommand(id, request), cancellationToken),
            "The candidate was rejected.");
}
