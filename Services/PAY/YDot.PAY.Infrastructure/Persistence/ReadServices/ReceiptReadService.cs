using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Receipts.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The read side of the receipt register - SCR-PAY-005.
///
/// THE DONOR NAME COMES FROM THE RECEIPT, NOT THE DONOR RECORD, and that is the whole point of
/// the snapshot columns. A receipt is a tax document as issued on a date; if the donor later
/// changes their name or address, the receipt they hold still says what it said, and a register
/// that rendered today's donor details beside a two-year-old receipt number would disagree with
/// the paper in the donor's hand.
/// </summary>
public sealed class ReceiptReadService(PaymentDbContext context, ICurrentUser currentUser)
    : IReceiptReadService
{
    public async Task<PagedResponse<ReceiptListItemResponse>> SearchAsync(
        ReceiptSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyFilter(BaseQuery(), filter, scope);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(receipt => receipt.ToListItemResponse(
                receipt.Donation?.DonationReference ?? string.Empty, canSeeSensitiveDonor))
            .ToList();

        return new PagedResponse<ReceiptListItemResponse>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<ReceiptDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var receipt = await BaseQuery()
            .Include(candidate => candidate.Supersedes)
            .Where(candidate => candidate.Id == id)
            .Where(candidate => !scope.IsOwnRecordsOnly || candidate.CreatedByUserId == scope.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return receipt?.ToDetailResponse(
            receipt.Donation?.DonationReference ?? string.Empty,

            // The number of the receipt this one replaced, so the screen can say "supersedes
            // RCPT/2026-27/00041" rather than showing a bare identifier nobody can look up.
            receipt.Supersedes?.ReceiptNumber,

            canSeeSensitiveDonor,
            ReceiptMappingConfig.PermittedActionsFor(receipt, currentUser.HasPermission));
    }

    public async Task<IReadOnlyList<ReceiptExportRow>> GetExportRowsAsync(
        ReceiptSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        const int MaximumExportRows = 50_000;

        var rows = await ApplySort(ApplyFilter(BaseQuery(), filter, scope), filter.Sort)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(receipt => receipt.ToExportRow(
            receipt.Donation?.DonationReference ?? string.Empty, canSeeSensitiveDonor))];
    }

    // =====================================================================================
    // Shaping
    // =====================================================================================

    /// <summary>
    /// The deliveries are always loaded, because the list row carries the delivery history: the
    /// register's most useful column is "did this actually reach the donor", and answering it
    /// per row from a second query would be one query per receipt.
    /// </summary>
    private IQueryable<Receipt> BaseQuery() =>
        context.Receipts
            .AsNoTracking()
            .Include(receipt => receipt.Deliveries)
            .Include(receipt => receipt.Donation);

    private static IQueryable<Receipt> ApplyFilter(
        IQueryable<Receipt> query, ReceiptSearchFilter filter, AccessScope scope)
    {
        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(receipt => receipt.CreatedByUserId == scope.UserId);
        }

        if (filter.IssueState.HasValue)
        {
            query = query.Where(receipt => receipt.Status == filter.IssueState.Value);
        }

        if (filter.DeliveryState.HasValue)
        {
            query = query.Where(receipt => receipt.DeliveryStatus == filter.DeliveryState.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.FinancialYear))
        {
            var financialYear = filter.FinancialYear.Trim();

            query = query.Where(receipt => receipt.FinancialYear == financialYear);
        }

        if (filter.CampaignId.HasValue)
        {
            query = query.Where(receipt =>
                receipt.Donation != null && receipt.Donation.CampaignId == filter.CampaignId.Value);
        }

        if (filter.IssuedFromUtc.HasValue)
        {
            query = query.Where(receipt =>
                receipt.IssuedAtUtc != null && receipt.IssuedAtUtc >= filter.IssuedFromUtc.Value);
        }

        if (filter.IssuedToUtc.HasValue)
        {
            query = query.Where(receipt =>
                receipt.IssuedAtUtc != null && receipt.IssuedAtUtc <= filter.IssuedToUtc.Value);
        }

        // The queue somebody has to work: a donor entitled to a tax document who never received
        // it will eventually ask, and it is better to find them first. Drafts are excluded -
        // an unissued receipt is not an undelivered one.
        if (filter.UndeliveredOnly == true)
        {
            query = query.Where(receipt =>
                receipt.Status == ReceiptStatus.Issued
                && (receipt.DeliveryStatus == ReceiptDeliveryStatus.NotSent
                    || receipt.DeliveryStatus == ReceiptDeliveryStatus.Failed));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(receipt =>
                (receipt.ReceiptNumber != null && receipt.ReceiptNumber.ToLower().Contains(term))
                || receipt.DonorName.ToLower().Contains(term)
                || receipt.DonorEmail.ToLower().Contains(term)
                || (receipt.Donation != null
                    && receipt.Donation.DonationReference.ToLower().Contains(term)));
        }

        return query;
    }

    private static IQueryable<Receipt> ApplySort(IQueryable<Receipt> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "number" => query.OrderBy(receipt => receipt.ReceiptNumber),
            "number_desc" => query.OrderByDescending(receipt => receipt.ReceiptNumber),
            "donor" => query.OrderBy(receipt => receipt.DonorName),
            "amount" => query.OrderBy(receipt => receipt.Amount.Amount),
            "amount_desc" => query.OrderByDescending(receipt => receipt.Amount.Amount),
            "issued" => query.OrderBy(receipt => receipt.IssuedAtUtc),
            "status" => query.OrderBy(receipt => receipt.Status)
                .ThenByDescending(receipt => receipt.CreatedAtUtc),

            // Newest issued first, with drafts - which have no issue date - falling to the end
            // rather than the top, because an unissued receipt is not the thing anybody opens
            // this register to look at.
            _ => query.OrderByDescending(receipt => receipt.IssuedAtUtc ?? receipt.CreatedAtUtc)
        };
}
