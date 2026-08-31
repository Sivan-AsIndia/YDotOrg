using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Audit.DTOs;
using YDot.IAM.Application.Features.Governance.Queries.GovernanceQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The audit trail. Read-only by design — there is no endpoint that writes, edits or deletes an
/// audit event, because a trail that can be corrected is not evidence of anything.
///
/// Events are written by the handlers themselves, in the same transaction as the change they
/// describe, so an action cannot succeed without leaving a record.
///
/// TWO THINGS ARE WORTH KNOWING ABOUT WHAT COMES BACK:
///
/// Payloads are REDACTED on write — password hashes, tokens, secrets and recovery codes never
/// reach this table, so no permission can reveal them here.
///
/// Detail is graded: without <c>iam.audit.view-sensitive</c> the before/after payloads are
/// withheld and only the event envelope is returned. Knowing that a colleague password was
/// reset is routine; seeing the contents of the change is not.
/// </summary>
[Route("api/v1/audit-events")]
[Authorize]
public sealed class AuditEventsController(GovernanceQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// Searches the trail.
    ///
    /// Organisation-scoped by the query filter like everything else. A SuperAdmin operating
    /// inside an Organisation sees that Organisation trail; platform-level events are reached
    /// through the platform route below.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.AuditView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AuditEventResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] AuditEventSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchAuditEventsQuery(filter), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.AuditView)]
    [ProducesResponseType(typeof(ApiResponse<AuditEventResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetAuditEventQuery(id), cancellationToken));

    /// <summary>
    /// The recent history of one record — what the "Activity" tab on a user or role shows.
    ///
    /// <paramref name="targetType"/> is matched against a known set inside the handler; it is
    /// not concatenated into anything.
    /// </summary>
    [HttpGet("trail/{targetType}/{targetId:guid}")]
    [HasPermission(PermissionCodes.AuditView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AuditEventResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrailAsync(
        string targetType, Guid targetId, [FromQuery] int take, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetAuditTrailForTargetQuery(targetType, targetId, take <= 0 ? 20 : take),
            cancellationToken));

    /// <summary>
    /// Exports the trail to CSV.
    ///
    /// The export is ITSELF audited, including the filter used — an unusual export is exactly
    /// the kind of thing a later investigation needs to see.
    /// </summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.AuditExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] AuditEventSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportAuditEventsQuery(filter), cancellationToken));
}
