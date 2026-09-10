using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Audit.DTOs;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.Governance.Queries.GovernanceQueries;

/// <summary>The access request queue.</summary>
public sealed record SearchAccessRequestsQuery(AccessRequestSearchFilter Filter);

/// <summary>One access request in full.</summary>
public sealed record GetAccessRequestQuery(Guid Id);

/// <summary>The access review queue.</summary>
public sealed record SearchAccessReviewsQuery(AccessReviewSearchFilter Filter);

/// <summary>One access review in full.</summary>
public sealed record GetAccessReviewQuery(Guid Id);

/// <summary>Every review campaign.</summary>
public sealed record GetAccessReviewCampaignsQuery;

/// <summary>One campaign.</summary>
public sealed record GetAccessReviewCampaignQuery(Guid Id);

/// <summary>One identifier-change request.</summary>
public sealed record GetLoginIdentifierChangeQuery(Guid Id);

/// <summary>Every identifier-change request for one user.</summary>
public sealed record GetLoginIdentifierChangesForUserQuery(Guid UserId);

/// <summary>The bulk job list.</summary>
public sealed record SearchBulkOperationsQuery(PaginationRequest Pagination);

/// <summary>One bulk job with its per-row outcomes.</summary>
public sealed record GetBulkOperationQuery(Guid Id);

/// <summary>The audit trail.</summary>
public sealed record SearchAuditEventsQuery(AuditEventSearchFilter Filter);

/// <summary>One audit row.</summary>
public sealed record GetAuditEventQuery(Guid Id);

/// <summary>The trail for one record, for a detail-screen history panel.</summary>
public sealed record GetAuditTrailForTargetQuery(string TargetType, Guid TargetId, int Take = 20);

/// <summary>CSV export of the audit trail.</summary>
public sealed record ExportAuditEventsQuery(AuditEventSearchFilter Filter);

/// <summary>The record types present in this Organisation's trail, for the filter dropdown.</summary>
public sealed record GetAuditTargetTypesQuery;

/// <summary>
/// The read side of governance, bulk jobs and the audit trail.
///
/// The caller identity is threaded into the queue queries rather than left to the client,
/// because "waiting on me" and "assigned to me" are the two views people actually work from,
/// and computing them on the server is what makes the independence rules visible in the list.
/// </summary>
public sealed class GovernanceQueryHandler(
    IGovernanceReadService governanceRead,
    IBulkOperationReadService bulkRead,
    IAuditReadService auditRead,
    IExportService exports,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<GovernanceQueryHandler> logger)
{
    public async Task<Result<PagedResponse<AccessRequestListItemResponse>>> HandleAsync(
        SearchAccessRequestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching access requests.");
        var result = await governanceRead.SearchRequestsAsync(query.Filter, currentUser.UserId, cancellationToken);
        logger.LogInformation("Access requests retrieved. TotalCount: {TotalCount}.", result.TotalCount);
        return Result.Success(result);
    }

    public async Task<Result<AccessRequestDetailResponse>> HandleAsync(
        GetAccessRequestQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving access request. RequestId: {RequestId}.", query.Id);
        var detail = await governanceRead.GetRequestAsync(query.Id, cancellationToken);
        if (detail is null)
        {
            logger.LogWarning("Access request not found. RequestId: {RequestId}.", query.Id);
            return Result.Failure<AccessRequestDetailResponse>(Error.NotFound("That request was not found."));
        }
        logger.LogInformation("Access request retrieved. RequestId: {RequestId}.", query.Id);
        return Result.Success(detail);
    }

    public async Task<Result<PagedResponse<AccessReviewListItemResponse>>> HandleAsync(
        SearchAccessReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching access reviews.");
        var result = await governanceRead.SearchReviewsAsync(query.Filter, currentUser.UserId, cancellationToken);
        logger.LogInformation("Access reviews retrieved. TotalCount: {TotalCount}.", result.TotalCount);
        return Result.Success(result);
    }

    public async Task<Result<AccessReviewDetailResponse>> HandleAsync(
        GetAccessReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving access review. ReviewId: {ReviewId}.", query.Id);
        var detail = await governanceRead.GetReviewAsync(query.Id, cancellationToken);
        if (detail is null)
        {
            logger.LogWarning("Access review not found. ReviewId: {ReviewId}.", query.Id);
            return Result.Failure<AccessReviewDetailResponse>(Error.NotFound("That review was not found."));
        }
        logger.LogInformation("Access review retrieved. ReviewId: {ReviewId}.", query.Id);
        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<AccessReviewCampaignResponse>>> HandleAsync(
        GetAccessReviewCampaignsQuery query, CancellationToken cancellationToken) =>
        Result.Success(await governanceRead.GetCampaignsAsync(cancellationToken));

    public async Task<Result<AccessReviewCampaignResponse>> HandleAsync(
        GetAccessReviewCampaignQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving access review campaign. CampaignId: {CampaignId}.", query.Id);
        var campaign = await governanceRead.GetCampaignAsync(query.Id, cancellationToken);
        if (campaign is null)
        {
            logger.LogWarning("Access review campaign not found. CampaignId: {CampaignId}.", query.Id);
            return Result.Failure<AccessReviewCampaignResponse>(Error.NotFound("That campaign was not found."));
        }
        logger.LogInformation("Access review campaign retrieved. CampaignId: {CampaignId}.", query.Id);
        return Result.Success(campaign);
    }

    public async Task<Result<LoginIdentifierChangeResponse>> HandleAsync(
        GetLoginIdentifierChangeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving login identifier change. RequestId: {RequestId}.", query.Id);
        var detail = await governanceRead.GetIdentifierChangeAsync(query.Id, cancellationToken);
        if (detail is null)
        {
            logger.LogWarning("Login identifier change not found. RequestId: {RequestId}.", query.Id);
            return Result.Failure<LoginIdentifierChangeResponse>(Error.NotFound("That request was not found."));
        }
        logger.LogInformation("Login identifier change retrieved. RequestId: {RequestId}.", query.Id);
        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<LoginIdentifierChangeResponse>>> HandleAsync(
        GetLoginIdentifierChangesForUserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A person may always see their own history; seeing somebody else needs the permission.
        if (query.UserId != currentUser.UserId
            && !currentUser.HasPermission(PermissionCodes.UsersChangeLoginIdentifier))
        {
            logger.LogWarning("Login identifier history access denied. UserId: {UserId}.", query.UserId);
            return Result.Failure<IReadOnlyList<LoginIdentifierChangeResponse>>(Error.Forbidden());
        }

        logger.LogInformation("Retrieving login identifier change history. UserId: {UserId}.", query.UserId);
        var result = await governanceRead.GetIdentifierChangesForUserAsync(query.UserId, cancellationToken);
        logger.LogInformation("Login identifier change history retrieved. UserId: {UserId}, Count: {Count}.", query.UserId, result.Count);
        return Result.Success(result);
    }

    public async Task<Result<PagedResponse<BulkOperationListItemResponse>>> HandleAsync(
        SearchBulkOperationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching bulk operations.");
        var result = await bulkRead.SearchAsync(query.Pagination, cancellationToken);
        logger.LogInformation("Bulk operations retrieved. TotalCount: {TotalCount}.", result.TotalCount);
        return Result.Success(result);
    }

    public async Task<Result<BulkOperationDetailResponse>> HandleAsync(
        GetBulkOperationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving bulk operation. BulkOperationId: {BulkOperationId}.", query.Id);
        var detail = await bulkRead.GetDetailAsync(query.Id, cancellationToken);
        if (detail is null)
        {
            logger.LogWarning("Bulk operation not found. BulkOperationId: {BulkOperationId}.", query.Id);
            return Result.Failure<BulkOperationDetailResponse>(Error.NotFound("That job was not found."));
        }
        logger.LogInformation("Bulk operation retrieved. BulkOperationId: {BulkOperationId}.", query.Id);
        return Result.Success(detail);
    }

    /// <summary>
    /// What the audit filter should offer, taken from what the trail holds.
    ///
    /// No permission of its own beyond the one on the endpoint: the answer is a list of type
    /// NAMES already visible in every row the caller can read.
    /// </summary>
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(
        GetAuditTargetTypesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving audit target types.");
        var result = await auditRead.GetTargetTypesAsync(cancellationToken);
        logger.LogInformation("Audit target types retrieved. Count: {Count}.", result.Count);
        return Result.Success(result);
    }

    public async Task<Result<PagedResponse<AuditEventResponse>>> HandleAsync(
        SearchAuditEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = currentUser.HasPermission(PermissionCodes.AuditViewSensitive);

        logger.LogInformation("Searching audit events. CanSeeSensitive: {CanSeeSensitive}.", canSeeSensitive);
        var result = await auditRead.SearchAsync(query.Filter, canSeeSensitive, cancellationToken);
        logger.LogInformation("Audit events retrieved. TotalCount: {TotalCount}.", result.TotalCount);
        return Result.Success(result);
    }

    public async Task<Result<AuditEventResponse>> HandleAsync(
        GetAuditEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = currentUser.HasPermission(PermissionCodes.AuditViewSensitive);
        logger.LogInformation("Retrieving audit event. AuditEventId: {AuditEventId}, CanSeeSensitive: {CanSeeSensitive}.", query.Id, canSeeSensitive);
        var detail = await auditRead.GetAsync(query.Id, canSeeSensitive, cancellationToken);
        if (detail is null)
        {
            logger.LogWarning("Audit event not found. AuditEventId: {AuditEventId}.", query.Id);
            return Result.Failure<AuditEventResponse>(Error.NotFound("That audit entry was not found."));
        }
        logger.LogInformation("Audit event retrieved. AuditEventId: {AuditEventId}.", query.Id);
        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<AuditEventResponse>>> HandleAsync(
        GetAuditTrailForTargetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving audit trail for target. TargetType: {TargetType}, TargetId: {TargetId}, Take: {Take}.", query.TargetType, query.TargetId, query.Take);
        var result = await auditRead.GetForTargetAsync(query.TargetType, query.TargetId, query.Take, cancellationToken);
        logger.LogInformation("Audit trail retrieved. TargetType: {TargetType}, TargetId: {TargetId}, Count: {Count}.", query.TargetType, query.TargetId, result.Count);
        return Result.Success(result);
    }

    /// <summary>
    /// Exports the audit trail.
    ///
    /// Exporting the audit trail is itself audited — which sounds circular but is the point:
    /// somebody taking a copy of the record of what everybody did is exactly the event a later
    /// investigation wants to find.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportAuditEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = currentUser.HasPermission(PermissionCodes.AuditViewSensitive);
        logger.LogInformation("Starting audit event export. CanSeeSensitive: {CanSeeSensitive}.", canSeeSensitive);

        var filter = query.Filter;
        filter.Page = 1;
        filter.PageSize = 100;

        var rows = new List<AuditExportRow>();
        PagedResponse<AuditEventResponse> page;

        do
        {
            page = await auditRead.SearchAsync(filter, canSeeSensitive, cancellationToken);
            logger.LogInformation("Audit export page retrieved. Page: {Page}, RowCount: {RowCount}.", filter.Page, page.Items.Count);

            rows.AddRange(page.Items.Select(item => new AuditExportRow(
                item.OccurredAtUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                item.TenantName,
                item.ActorDisplayName,
                item.ActorScope.ToString(),
                item.ActionCode,
                item.TargetType,
                item.TargetDisplayName,
                item.ResultDisplay,
                item.Reason,
                item.IpAddress,
                item.ClientType.ToString(),
                item.CorrelationId)));

            filter.Page++;
        }
        // Capped, so one click cannot pull an unbounded trail into memory.
        while (filter.Page <= page.TotalPages && filter.Page <= 200);

        var reference = tokenHasher.GenerateReference("EXP");
        var file = exports.ToCsv(rows, "audit-trail", reference);

        await audit.WriteAsync(
            AuditActionCodes.AuditExported, nameof(AuditEvent), null, null,
            new { Action = "Exported", RowCount = rows.Count, Reference = reference, canSeeSensitive },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Audit event export completed. RowCount: {RowCount}.", rows.Count);
        return Result.Success(file);
    }
}
