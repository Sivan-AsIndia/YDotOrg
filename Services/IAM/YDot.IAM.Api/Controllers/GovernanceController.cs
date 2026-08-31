using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Governance.Commands.AccessRequests;
using YDot.IAM.Application.Features.Governance.Commands.AccessReviews;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Governance.Queries.GovernanceQueries;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Access governance: requests for access, and periodic re-certification of access already held.
///
/// TWO RULES RUN THROUGH EVERY ENDPOINT HERE, and they are the reason the module exists:
///
/// <b>Independence</b> — nobody approves their own request, and nobody certifies their own
/// access. Enforced in the handler against the persisted requester, never against anything the
/// client sent.
///
/// <b>Optimistic concurrency</b> — every state-changing call carries ExpectedVersion. Two
/// approvers opening the same request see the second one rejected rather than silently
/// overwriting the first. A stale version comes back 409, and the UI is expected to reload.
/// </summary>
[Route("api/v1/governance")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class GovernanceController(
    AccessRequestCommandHandler accessRequests,
    AccessReviewCommandHandler accessReviews,
    GovernanceQueryHandler queries) : ApiControllerBase
{
    // =================================================================================
    // Access requests
    // =================================================================================

    [HttpGet("access-requests")]
    [HasPermission(PermissionCodes.AccessRequestsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AccessRequestListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAccessRequestsAsync(
        [FromQuery] AccessRequestSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchAccessRequestsQuery(filter), cancellationToken));

    /// <summary>
    /// One access request, with the actions THIS caller may take on it.
    ///
    /// PermittedActions is computed server-side from the state, the caller permissions and the
    /// independence rule, so the buttons Angular renders and the rules the API enforces cannot
    /// drift apart.
    /// </summary>
    [HttpGet("access-requests/{id:guid}", Name = nameof(GetAccessRequestAsync))]
    [HasPermission(PermissionCodes.AccessRequestsView)]
    [ProducesResponseType(typeof(ApiResponse<AccessRequestDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccessRequestAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAccessRequestQuery(id), cancellationToken));

    [HttpPost("access-requests")]
    [HasPermission(PermissionCodes.AccessRequestsCreate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAccessRequestAsync(
        [FromBody] CreateAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new CreateAccessRequestCommand(request), cancellationToken));

    /// <summary>Edits a draft. Once submitted a request is immutable — reject and re-raise.</summary>
    [HttpPut("access-requests/{id:guid}")]
    [HasPermission(PermissionCodes.AccessRequestsCreate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAccessRequestAsync(
        Guid id, [FromBody] UpdateAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new UpdateAccessRequestCommand(id, request), cancellationToken));

    [HttpPost("access-requests/{id:guid}/submit")]
    [HasPermission(PermissionCodes.AccessRequestsSubmit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitAccessRequestAsync(
        Guid id, [FromBody] SubmitAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new SubmitAccessRequestCommand(id, request), cancellationToken));

    /// <summary>
    /// Approves or rejects, and on approval APPLIES the access in the same transaction.
    ///
    /// Applying it here rather than in a later job is deliberate: an approval that is recorded
    /// but not granted is the worst of both worlds — the audit trail says yes and the user
    /// still cannot work.
    /// </summary>
    [HttpPost("access-requests/{id:guid}/decide")]
    [HasPermission(PermissionCodes.AccessRequestsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DecideAccessRequestAsync(
        Guid id, [FromBody] DecideAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new DecideAccessRequestCommand(id, request), cancellationToken));

    /// <summary>
    /// Sends a request back to the requester for more information.
    ///
    /// NOT A REJECTION. A rejection is a decision — the answer is no. A return says the approver
    /// cannot answer yet, almost always because the justification does not explain what the
    /// access is for. The request keeps its number and its history and goes back to be improved,
    /// which is both more accurate and less discouraging than refusing it.
    ///
    /// Subject to the same independence rule as a decision: nobody returns their own request.
    /// </summary>
    [HttpPost("access-requests/{id:guid}/return")]
    [HasPermission(PermissionCodes.AccessRequestsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReturnAccessRequestAsync(
        Guid id, [FromBody] ReturnAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new ReturnAccessRequestCommand(id, request), cancellationToken));

    [HttpPost("access-requests/{id:guid}/withdraw")]
    [HasPermission(PermissionCodes.AccessRequestsWithdraw)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> WithdrawAccessRequestAsync(
        Guid id, [FromBody] WithdrawAccessRequestRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessRequests.HandleAsync(
            new WithdrawAccessRequestCommand(id, request), cancellationToken));

    // =================================================================================
    // Access reviews (re-certification)
    // =================================================================================

    /// <summary>
    /// Opens a campaign and generates one review row per access holding in its scope.
    ///
    /// Generating up front, rather than lazily, is what makes "how far through are we" a
    /// countable number instead of a guess.
    /// </summary>
    [HttpPost("access-review-campaigns")]
    [HasPermission(PermissionCodes.AccessReviewsCreate)]
    [ProducesResponseType(typeof(ApiResponse<AccessReviewCampaignResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCampaignAsync(
        [FromBody] CreateAccessReviewCampaignRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new CreateAccessReviewCampaignCommand(request), cancellationToken));

    [HttpGet("access-review-campaigns")]
    [HasPermission(PermissionCodes.AccessReviewsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AccessReviewCampaignResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCampaignsAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAccessReviewCampaignsQuery(), cancellationToken));

    [HttpGet("access-review-campaigns/{id:guid}")]
    [HasPermission(PermissionCodes.AccessReviewsView)]
    [ProducesResponseType(typeof(ApiResponse<AccessReviewCampaignResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCampaignAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAccessReviewCampaignQuery(id), cancellationToken));

    /// <summary>
    /// Closes a campaign. Anything still undecided is recorded as such rather than assumed
    /// approved — silence is not certification.
    /// </summary>
    [HttpPost("access-review-campaigns/{id:guid}/close")]
    [HasPermission(PermissionCodes.AccessReviewsCreate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CloseCampaignAsync(
        Guid id, [FromBody] CloseAccessReviewCampaignRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new CloseAccessReviewCampaignCommand(id, request), cancellationToken));

    [HttpGet("access-reviews")]
    [HasPermission(PermissionCodes.AccessReviewsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AccessReviewListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAccessReviewsAsync(
        [FromQuery] AccessReviewSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchAccessReviewsQuery(filter), cancellationToken));

    [HttpGet("access-reviews/{id:guid}")]
    [HasPermission(PermissionCodes.AccessReviewsView)]
    [ProducesResponseType(typeof(ApiResponse<AccessReviewDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccessReviewAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAccessReviewQuery(id), cancellationToken));

    /// <summary>Adds a single ad-hoc review outside a campaign.</summary>
    [HttpPost("access-reviews")]
    [HasPermission(PermissionCodes.AccessReviewsCreate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAccessReviewAsync(
        [FromBody] CreateAccessReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(new CreateAccessReviewCommand(request), cancellationToken));

    /// <summary>
    /// Certifies or revokes. A REVOKE decision removes the access immediately, in the same
    /// transaction as the decision — see the note on DecideAccessRequest.
    /// </summary>
    [HttpPost("access-reviews/{id:guid}/decide")]
    [HasPermission(PermissionCodes.AccessReviewsDecide)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DecideAccessReviewAsync(
        Guid id, [FromBody] DecideAccessReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new DecideAccessReviewCommand(id, request), cancellationToken));

    /// <summary>
    /// Hands a review to somebody better placed to answer it.
    ///
    /// The ORIGINAL reviewer is kept on the record, because "who was asked" and "who answered"
    /// are different questions and an audit of a certification wants both.
    ///
    /// It cannot be handed to the subject of the review — that would be somebody certifying
    /// their own access by the back door.
    /// </summary>
    [HttpPost("access-reviews/{id:guid}/delegate")]
    [HasPermission(PermissionCodes.AccessReviewsDecide)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DelegateAccessReviewAsync(
        Guid id, [FromBody] DelegateAccessReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new DelegateAccessReviewCommand(id, request), cancellationToken));

    /// <summary>
    /// Escalates a review the reviewer cannot answer alone.
    ///
    /// The same handover as a delegation, recorded differently on purpose. A delegation says
    /// "you are better placed to answer this"; an escalation says "this access looks wrong and
    /// removing it is above my authority" — which is exactly what a governance report needs to
    /// be able to count, and what would vanish if both were stored as one event.
    /// </summary>
    [HttpPost("access-reviews/{id:guid}/escalate")]
    [HasPermission(PermissionCodes.AccessReviewsDecide)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EscalateAccessReviewAsync(
        Guid id, [FromBody] EscalateAccessReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new EscalateAccessReviewCommand(id, request), cancellationToken));

    [HttpPost("access-reviews/{id:guid}/cancel")]
    [HasPermission(PermissionCodes.AccessReviewsCancel)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAccessReviewAsync(
        Guid id, [FromBody] CancelAccessReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await accessReviews.HandleAsync(
            new CancelAccessReviewCommand(id, request), cancellationToken));

    // =================================================================================
    // Bulk operations
    // =================================================================================

    [HttpGet("bulk-operations")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<BulkOperationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchBulkOperationsAsync(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchBulkOperationsQuery(pagination), cancellationToken));

    /// <summary>
    /// One bulk job with its per-row outcomes.
    ///
    /// Bulk actions are partial by design: 47 of 50 succeeding is a real result, and the three
    /// failures are listed with their reasons rather than collapsing the whole job to "failed".
    /// </summary>
    [HttpGet("bulk-operations/{id:guid}")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<BulkOperationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBulkOperationAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetBulkOperationQuery(id), cancellationToken));
}
