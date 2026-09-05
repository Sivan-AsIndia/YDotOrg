using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Audit.DTOs;
using YDot.IAM.Application.Features.Governance.Mappings;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the audit trail.
///
/// <paramref name="canSeeSensitive"/> does two things, and the second is the important one:
/// it decides whether rows flagged sensitive appear AT ALL, and it decides whether the
/// redacted metadata is returned. Somebody with plain audit access sees that an action
/// happened; somebody with the sensitive permission sees what changed.
/// </summary>
public sealed class AuditReadService(IamDbContext context) : IAuditReadService
{
    public async Task<PagedResponse<AuditEventResponse>> SearchAsync(
        AuditEventSearchFilter filter, bool canSeeSensitive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.AuditEvents.AsNoTracking();

        // Sensitive rows are excluded entirely without the permission, rather than returned
        // with their detail stripped. A row that says "password reset by administrator" is
        // itself information.
        if (!canSeeSensitive)
        {
            query = query.Where(item => !item.IsSensitive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(item =>
                item.ActionCode.ToLower().Contains(term)
                || (item.ActorDisplayName != null && item.ActorDisplayName.ToLower().Contains(term))
                || (item.TargetDisplayName != null && item.TargetDisplayName.ToLower().Contains(term))
                || item.TargetType.ToLower().Contains(term));
        }

        if (filter.ActorUserId.HasValue)
        {
            query = query.Where(item => item.ActorUserId == filter.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionCode))
        {
            query = query.Where(item => item.ActionCode == filter.ActionCode);
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            query = query.Where(item => item.TargetType == filter.TargetType);
        }

        if (filter.TargetId.HasValue)
        {
            query = query.Where(item => item.TargetId == filter.TargetId.Value);
        }

        if (filter.Result.HasValue)
        {
            query = query.Where(item => item.Result == filter.Result.Value);
        }

        if (filter.IsSensitive.HasValue && canSeeSensitive)
        {
            query = query.Where(item => item.IsSensitive == filter.IsSensitive.Value);
        }

        // The correlation id is how one request is followed across every row it produced,
        // which is the first thing anybody reaches for when investigating.
        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            query = query.Where(item => item.CorrelationId == filter.CorrelationId);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(item => item.OccurredAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(item => item.OccurredAtUtc <= filter.ToUtc.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(item => item.OccurredAtUtc)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(item => new
            {
                Event = item,
                TenantName = item.TenantId == null
                    ? null
                    : context.Tenants.IgnoreQueryFilters()
                        .Where(tenant => tenant.Id == item.TenantId)
                        .Select(tenant => tenant.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => ToResponse(row.Event, row.TenantName, canSeeSensitive))
            .ToList();

        return new PagedResponse<AuditEventResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<AuditEventResponse?> GetAsync(
        Guid id, bool canSeeSensitive, CancellationToken cancellationToken)
    {
        var auditEvent = await context.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (auditEvent is null || (auditEvent.IsSensitive && !canSeeSensitive))
        {
            return null;
        }

        var tenantName = auditEvent.TenantId.HasValue
            ? await context.Tenants.IgnoreQueryFilters()
                .Where(tenant => tenant.Id == auditEvent.TenantId.Value)
                .Select(tenant => tenant.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return ToResponse(auditEvent, tenantName, canSeeSensitive);
    }

    /// <summary>
    /// The trail for one record, for the "history" panel on a detail screen.
    ///
    /// Sensitive rows are excluded here unconditionally: this panel appears on ordinary
    /// screens whose permission is about the record, not about the audit trail.
    /// </summary>
    public async Task<IReadOnlyList<AuditEventResponse>> GetForTargetAsync(
        string targetType, Guid targetId, int take, CancellationToken cancellationToken)
    {
        var rows = await context.AuditEvents
            .AsNoTracking()
            .Where(item => item.TargetType == targetType && item.TargetId == targetId)
            .Where(item => !item.IsSensitive)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => ToResponse(row, null, canSeeSensitive: false))];
    }

    /// <summary>
    /// The distinct record types in this Organisation's trail.
    ///
    /// The global query filter scopes it to the caller's Organisation, exactly like the search
    /// above, so this never reveals that another Organisation has records of a type yours does
    /// not. DISTINCT on an indexed column over a table that is only ever appended to, ordered so
    /// the dropdown is stable between loads rather than reordering as new rows arrive.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTargetTypesAsync(CancellationToken cancellationToken) =>
        await context.AuditEvents
            .AsNoTracking()
            .Where(item => item.TargetType != null && item.TargetType != "")
            .Select(item => item.TargetType)
            .Distinct()
            .OrderBy(targetType => targetType)
            .ToListAsync(cancellationToken);

    private static AuditEventResponse ToResponse(
        Domain.Entities.AuditEvent auditEvent, string? tenantName, bool canSeeSensitive) =>
        new(
            auditEvent.Id,
            auditEvent.TenantId,
            tenantName,
            auditEvent.BusinessUnitId,
            auditEvent.ActorUserId,
            auditEvent.ActorDisplayName,
            auditEvent.ActorScope,
            auditEvent.ActionCode,
            GovernanceMappingConfig.Humanise(
                auditEvent.ActionCode.Split('.').LastOrDefault() ?? auditEvent.ActionCode),
            auditEvent.TargetType,
            auditEvent.TargetId,
            auditEvent.TargetDisplayName,
            auditEvent.Result,
            auditEvent.Result.ToString(),
            auditEvent.Reason,
            auditEvent.CorrelationId,
            auditEvent.OccurredAtUtc,
            auditEvent.IpAddress,
            auditEvent.UserAgent,
            auditEvent.ClientType,
            auditEvent.SessionId,
            auditEvent.IsSensitive,
            auditEvent.RequestPath,
            // The metadata is already redacted on the way in. Withholding it without the
            // permission is a second, coarser layer on top of that.
            canSeeSensitive ? auditEvent.Metadata : null);
}

/// <summary>Read side for bulk user administration jobs.</summary>
public sealed class BulkOperationReadService(IamDbContext context) : IBulkOperationReadService
{
    public async Task<PagedResponse<BulkOperationListItemResponse>> SearchAsync(
        PaginationRequest pagination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pagination);

        var query = context.BulkOperations.AsNoTracking();

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(operation => new
            {
                Operation = operation,
                RequestedByName = context.Users.IgnoreQueryFilters()
                    .Where(user => user.Id == operation.RequestedByUserId)
                    .Select(user => user.DisplayName).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new BulkOperationListItemResponse(
                row.Operation.Id,
                row.Operation.OperationNumber,
                row.Operation.ActionType,
                GovernanceMappingConfig.Humanise(row.Operation.ActionType.ToString()),
                row.Operation.Status,
                GovernanceMappingConfig.Humanise(row.Operation.Status.ToString()),
                row.Operation.TotalItemCount,
                row.Operation.ProcessedItemCount,
                row.Operation.SucceededItemCount,
                row.Operation.FailedItemCount,
                row.Operation.PercentComplete,
                row.Operation.CreatedAtUtc,
                row.RequestedByName,
                row.Operation.CompletedAtUtc,
                row.Operation.Version))
            .ToList();

        return new PagedResponse<BulkOperationListItemResponse>(
            items, total, pagination.Page, pagination.PageSize);
    }

    public async Task<BulkOperationDetailResponse?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var operation = await context.BulkOperations
            .AsNoTracking()
            .Include(item => item.Items.OrderBy(row => row.RowNumber))
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (operation is null)
        {
            return null;
        }

        var requestedByName = await context.Users
            .IgnoreQueryFilters()
            .Where(user => user.Id == operation.RequestedByUserId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        var actions = operation.Status switch
        {
            Domain.Enums.BulkOperationStatus.Draft => new[] { "View", "Validate", "Cancel" },
            Domain.Enums.BulkOperationStatus.Validated => ["View", "Apply", "Cancel"],
            Domain.Enums.BulkOperationStatus.Queued => ["View", "Cancel"],
            Domain.Enums.BulkOperationStatus.Running => ["View"],
            _ => ["View", "Download"]
        };

        return new BulkOperationDetailResponse(
            operation.Id,
            operation.OperationNumber,
            operation.ActionType,
            GovernanceMappingConfig.Humanise(operation.ActionType.ToString()),
            operation.Status,
            GovernanceMappingConfig.Humanise(operation.Status.ToString()),
            operation.SourceFileName,
            operation.TotalItemCount,
            operation.ProcessedItemCount,
            operation.SucceededItemCount,
            operation.FailedItemCount,
            operation.SkippedItemCount,
            operation.PercentComplete,
            operation.ValidatedAtUtc,
            operation.StartedAtUtc,
            operation.CompletedAtUtc,
            operation.FailureSummary,
            operation.CreatedAtUtc,
            requestedByName,
            operation.Version,
            [
                .. operation.Items.Select(item => new BulkOperationItemResponse(
                    item.Id, item.RowNumber, item.UserId, item.SourceIdentifier,
                    item.IsValid, item.ValidationMessage, item.IsProcessed,
                    item.Succeeded, item.WasSkipped, item.ResultMessage))
            ],
            actions);
    }
}
