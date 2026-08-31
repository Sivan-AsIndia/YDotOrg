using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Refunds.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The read side of the refund and chargeback registers - SCR-PAY-006 and SCR-PAY-008.
///
/// THE PERMITTED ACTIONS ON A REFUND ARE PER-RECORD, NOT PER-PERMISSION, which is why the
/// caller's user id is threaded all the way down here. Approve and Reject are withheld from the
/// person who raised the case whatever permissions they hold - money leaving the organisation
/// needs two people, and that is a rule about WHO relative to THIS RECORD, which no permission
/// code can express.
///
/// Deciding it here as well as in the handler is what stops the screen drawing a button that
/// will answer 409.
/// </summary>
public sealed class RefundReadService(
    PaymentDbContext context,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRefundReadService
{
    // =====================================================================================
    // Refunds
    // =====================================================================================

    public async Task<PagedResponse<RefundCaseListItemResponse>> SearchRefundsAsync(
        RefundSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyRefundFilter(RefundBaseQuery(), filter, scope);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplyRefundSort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(refundCase => refundCase.ToListItemResponse(refundCase.Donation, canSeeSensitiveDonor))
            .ToList();

        return new PagedResponse<RefundCaseListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<RefundCaseDetailResponse?> GetRefundDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var refundCase = await RefundBaseQuery()
            .Where(candidate => candidate.Id == id)
            .Where(candidate => !scope.IsOwnRecordsOnly
                                || candidate.RequestedByUserId == scope.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return refundCase?.ToDetailResponse(
            refundCase.Donation,
            canSeeSensitiveDonor,
            RefundMappingConfig.PermittedActionsFor(
                refundCase, currentUser.UserId, currentUser.HasPermission));
    }

    public async Task<IReadOnlyList<RefundExportRow>> GetRefundExportRowsAsync(
        RefundSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        const int MaximumExportRows = 50_000;

        var rows = await ApplyRefundSort(ApplyRefundFilter(RefundBaseQuery(), filter, scope), filter.Sort)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(refundCase => refundCase.ToExportRow(refundCase.Donation))];
    }

    // =====================================================================================
    // Chargebacks
    // =====================================================================================

    public async Task<PagedResponse<ChargebackCaseListItemResponse>> SearchChargebacksAsync(
        ChargebackSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var now = clock.UtcNow;

        var query = ApplyChargebackFilter(ChargebackBaseQuery(), filter, scope, now);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplyChargebackSort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(chargeback => chargeback.ToListItemResponse(chargeback.Donation, now))
            .ToList();

        return new PagedResponse<ChargebackCaseListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<ChargebackCaseDetailResponse?> GetChargebackDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = clock.UtcNow;

        var chargeback = await ChargebackBaseQuery()
            .Where(candidate => candidate.Id == id)
            .Where(candidate => !scope.IsOwnRecordsOnly
                                || candidate.AssignedToUserId == scope.UserId
                                || candidate.CreatedByUserId == scope.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return chargeback?.ToDetailResponse(
            chargeback.Donation,
            now,
            canSeeSensitiveDonor,
            RefundMappingConfig.PermittedActionsFor(chargeback, now, currentUser.HasPermission));
    }

    // =====================================================================================
    // Shaping
    // =====================================================================================

    /// <summary>
    /// The donation is always loaded because every refund row shows the donation reference, the
    /// donor and the original amount beside the refund - a refund of 10,000 means nothing without
    /// the 50,000 it came out of.
    /// </summary>
    private IQueryable<RefundCase> RefundBaseQuery() =>
        context.RefundCases
            .AsNoTracking()
            .Include(refundCase => refundCase.Donation);

    private IQueryable<ChargebackCase> ChargebackBaseQuery() =>
        context.ChargebackCases
            .AsNoTracking()
            .Include(chargeback => chargeback.Donation);

    private static IQueryable<RefundCase> ApplyRefundFilter(
        IQueryable<RefundCase> query, RefundSearchFilter filter, AccessScope scope)
    {
        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(refundCase => refundCase.RequestedByUserId == scope.UserId);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(refundCase => refundCase.Status == filter.Status.Value);
        }

        if (filter.Reason.HasValue)
        {
            query = query.Where(refundCase => refundCase.Reason == filter.Reason.Value);
        }

        if (filter.DonationId.HasValue)
        {
            query = query.Where(refundCase => refundCase.DonationId == filter.DonationId.Value);
        }

        if (filter.RequestedFromUtc.HasValue)
        {
            query = query.Where(refundCase => refundCase.RequestedAtUtc >= filter.RequestedFromUtc.Value);
        }

        if (filter.RequestedToUtc.HasValue)
        {
            query = query.Where(refundCase => refundCase.RequestedAtUtc <= filter.RequestedToUtc.Value);
        }

        // The states are written out rather than calling the entity's IsOpen, because a computed
        // property has no SQL translation and EF would evaluate it in memory over every row.
        if (filter.OpenOnly == true)
        {
            query = query.Where(refundCase =>
                refundCase.Status == RefundStatus.Requested
                || refundCase.Status == RefundStatus.Approved
                || refundCase.Status == RefundStatus.Processing);
        }

        // A completed refund whose receipt was never corrected leaves the donor holding a tax
        // document for money they no longer gave - which is a compliance problem, not a tidiness
        // one, and is why it has its own queue.
        if (filter.AwaitingReceiptCorrection == true)
        {
            query = query.Where(refundCase =>
                refundCase.Status == RefundStatus.Completed && !refundCase.ReceiptCorrected);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(refundCase =>
                refundCase.CaseReference.ToLower().Contains(term)
                || refundCase.Donation.DonationReference.ToLower().Contains(term)
                || refundCase.Donation.DonorName.ToLower().Contains(term));
        }

        return query;
    }

    private static IQueryable<RefundCase> ApplyRefundSort(IQueryable<RefundCase> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "reference" => query.OrderBy(refundCase => refundCase.CaseReference),
            "amount" => query.OrderBy(refundCase => refundCase.Amount.Amount),
            "amount_desc" => query.OrderByDescending(refundCase => refundCase.Amount.Amount),
            "status" => query.OrderBy(refundCase => refundCase.Status)
                .ThenByDescending(refundCase => refundCase.RequestedAtUtc),
            "requested" => query.OrderBy(refundCase => refundCase.RequestedAtUtc),
            _ => query.OrderByDescending(refundCase => refundCase.RequestedAtUtc)
        };

    private static IQueryable<ChargebackCase> ApplyChargebackFilter(
        IQueryable<ChargebackCase> query,
        ChargebackSearchFilter filter,
        AccessScope scope,
        DateTimeOffset now)
    {
        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(chargeback =>
                chargeback.AssignedToUserId == scope.UserId
                || chargeback.CreatedByUserId == scope.UserId);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(chargeback => chargeback.Status == filter.Status.Value);
        }

        if (filter.DonationId.HasValue)
        {
            query = query.Where(chargeback => chargeback.DonationId == filter.DonationId.Value);
        }

        if (filter.AssignedToUserId.HasValue)
        {
            query = query.Where(chargeback => chargeback.AssignedToUserId == filter.AssignedToUserId.Value);
        }

        if (filter.OpenOnly == true)
        {
            query = query.Where(chargeback =>
                chargeback.Status == ChargebackStatus.Opened
                || chargeback.Status == ChargebackStatus.EvidenceRequired
                || chargeback.Status == ChargebackStatus.UnderReview);
        }

        // Overdue means the deadline has passed and nothing was submitted. A case whose evidence
        // went in on time is not overdue however long the bank then takes to decide it.
        if (filter.OverdueOnly == true)
        {
            query = query.Where(chargeback =>
                chargeback.EvidenceDueAtUtc != null
                && chargeback.EvidenceDueAtUtc < now
                && chargeback.EvidenceSubmittedAtUtc == null
                && (chargeback.Status == ChargebackStatus.Opened
                    || chargeback.Status == ChargebackStatus.EvidenceRequired
                    || chargeback.Status == ChargebackStatus.UnderReview));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(chargeback =>
                chargeback.CaseReference.ToLower().Contains(term)
                || (chargeback.GatewayDisputeReference != null
                    && chargeback.GatewayDisputeReference.ToLower().Contains(term))
                || chargeback.Donation.DonationReference.ToLower().Contains(term)
                || chargeback.Donation.DonorName.ToLower().Contains(term));
        }

        return query;
    }

    /// <summary>
    /// Chargebacks sort by URGENCY by default, not by date.
    ///
    /// A chargeback has a deadline set by the bank, and once it passes the case is lost whatever
    /// anybody does. Sorting newest-first - correct everywhere else in this module - would put
    /// the case with two days left below one opened this morning with thirty.
    ///
    /// Cases with no deadline sort last rather than first, because a null deadline is the absence
    /// of urgency, not infinite urgency.
    /// </summary>
    private static IQueryable<ChargebackCase> ApplyChargebackSort(
        IQueryable<ChargebackCase> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "reference" => query.OrderBy(chargeback => chargeback.CaseReference),
            "amount" => query.OrderBy(chargeback => chargeback.DisputedAmount.Amount),
            "amount_desc" => query.OrderByDescending(chargeback => chargeback.DisputedAmount.Amount),
            "status" => query.OrderBy(chargeback => chargeback.Status)
                .ThenByDescending(chargeback => chargeback.OpenedAtUtc),
            "opened" => query.OrderBy(chargeback => chargeback.OpenedAtUtc),
            "opened_desc" => query.OrderByDescending(chargeback => chargeback.OpenedAtUtc),
            _ => query
                .OrderBy(chargeback => chargeback.EvidenceDueAtUtc == null)
                .ThenBy(chargeback => chargeback.EvidenceDueAtUtc)
                .ThenByDescending(chargeback => chargeback.OpenedAtUtc)
        };
}
