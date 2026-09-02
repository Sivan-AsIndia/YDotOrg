using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Receipts.Mappings;
using YDot.PAY.Application.Features.Shared.Mappings;
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

    // =====================================================================================
    // The Receipt Register - SCR-PAY-005 as the workflow document describes it
    // =====================================================================================

    /// <summary>
    /// Issued receipts and failed payments, together, with the four totals.
    ///
    /// TWO QUERIES AND A MERGE, NOT A UNION IN SQL. The two halves live in different tables with
    /// different columns and neither is a subset of the other, so a database-level UNION would
    /// need a projection wide enough for both and would still have to be re-sorted afterwards.
    /// Reading each half's page and merging is simpler to read and, at register page sizes,
    /// indistinguishable in cost.
    ///
    /// OVER-FETCHING IS DELIBERATE. Each half is asked for (Skip + Take) rows rather than Take,
    /// because a merge sorted by date cannot know in advance how many of the page's rows come
    /// from which side - the first eight of the combined set could be eight receipts, eight
    /// failures, or any mix.
    /// </summary>
    public async Task<ReceiptRegisterResponse> GetRegisterAsync(
        ReceiptRegisterFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var wantsSuccess = !string.Equals(filter.Status, "Failed", StringComparison.OrdinalIgnoreCase);
        var wantsFailed = !string.Equals(filter.Status, "Success", StringComparison.OrdinalIgnoreCase);

        var receipts = BuildReceiptHalf(filter, scope);
        var failures = BuildFailureHalf(filter, scope);

        var successCount = wantsSuccess ? await receipts.CountAsync(cancellationToken) : 0;
        var failedCount = wantsFailed ? await failures.CountAsync(cancellationToken) : 0;

        var ceiling = filter.Skip + filter.PageSize;

        var receiptRows = wantsSuccess
            ? await receipts
                .OrderByDescending(receipt => receipt.IssuedAtUtc ?? receipt.CreatedAtUtc)
                .Take(ceiling)
                .ToListAsync(cancellationToken)
            : [];

        var failureRows = wantsFailed
            ? await failures
                .OrderByDescending(intent => intent.LastAttemptAtUtc ?? intent.CreatedAtUtc)
                .Take(ceiling)
                .ToListAsync(cancellationToken)
            : [];

        var merged = receiptRows
            .Select(receipt => ToRegisterRow(receipt, canSeeSensitiveDonor))
            .Concat(failureRows.Select(intent => ToRegisterRow(intent, canSeeSensitiveDonor)))
            .OrderByDescending(row => row.ReceiptDateUtc ?? DateTimeOffset.MinValue)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToList();

        // SUCCESSFUL MONEY ONLY, and summed over the whole scope rather than the page. A failed
        // payment moved nothing; including it would overstate what the charity actually received.
        var totalAmount = await BuildReceiptHalf(filter, scope)
            .Select(receipt => receipt.Amount.Amount)
            .SumAsync(cancellationToken);

        var currency = receiptRows.Count > 0
            ? receiptRows[0].Amount.CurrencyCode
            : failureRows.Count > 0 ? failureRows[0].Amount.CurrencyCode : "INR";

        var summary = new ReceiptRegisterSummaryResponse(
            successCount + failedCount,
            MoneyResponse.Plain(totalAmount, currency),
            successCount,
            failedCount);

        return new ReceiptRegisterResponse(
            new PagedResponse<ReceiptRegisterRowResponse>(
                merged, successCount + failedCount, filter.Page, filter.PageSize),
            summary,
            PermittedRegisterActions());
    }

    /// <summary>
    /// The Success half: receipts that were actually issued.
    ///
    /// DRAFTS AND SUBMITTED RECEIPTS ARE EXCLUDED. The register reports outcomes, and a receipt
    /// still working its way through approval is not yet an outcome anybody can be told about.
    /// A voided receipt is excluded too - it has been withdrawn, so reporting it as a success
    /// would contradict the document it superseded.
    /// </summary>
    private IQueryable<Receipt> BuildReceiptHalf(ReceiptRegisterFilter filter, AccessScope scope)
    {
        var query = BaseQuery()
            .Where(receipt => receipt.Status == ReceiptStatus.Issued
                || receipt.Status == ReceiptStatus.Corrected);

        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(receipt => receipt.CreatedByUserId == scope.UserId);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(receipt => receipt.IssuedAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(receipt => receipt.IssuedAtUtc <= filter.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(receipt =>
                (receipt.ReceiptNumber != null && receipt.ReceiptNumber.ToLower().Contains(term))
                || receipt.DonorName.ToLower().Contains(term)
                || receipt.DonorEmail.ToLower().Contains(term));
        }

        return query;
    }

    /// <summary>
    /// The Failed half: donation intents the gateway refused.
    ///
    /// PENDING IS NOT HERE. An outstanding payment has no result yet, so it belongs on the
    /// Payment Queue where somebody can still act on it - not in a register of what happened.
    /// </summary>
    private IQueryable<DonationIntent> BuildFailureHalf(ReceiptRegisterFilter filter, AccessScope scope)
    {
        var query = context.DonationIntents
            .AsNoTracking()
            .Where(intent => intent.Status == DonationIntentStatus.Failed);

        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(intent => intent.CreatedByUserId == scope.UserId);
        }

        if (filter.CampaignId.HasValue)
        {
            query = query.Where(intent => intent.CampaignId == filter.CampaignId.Value);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(intent => intent.CreatedAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(intent => intent.CreatedAtUtc <= filter.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(intent =>
                intent.IntentReference.ToLower().Contains(term)
                || intent.DonorName.ToLower().Contains(term)
                || intent.Email.ToLower().Contains(term));
        }

        return query;
    }

    private static ReceiptRegisterRowResponse ToRegisterRow(Receipt receipt, bool canSeeSensitiveDonor) =>
        new(receipt.Id,
            receipt.ReceiptNumber,
            receipt.ReceiptNumber ?? receipt.Donation?.DonationReference ?? string.Empty,
            receipt.IssuedAtUtc,

            // THE NAME AS PRINTED. A receipt is a document as issued on a date; showing today's
            // donor details beside a two-year-old receipt number would disagree with the paper
            // the donor is holding.
            receipt.DonorName,

            PaymentMappingConfig.ToResponse(receipt.Amount),
            "Success",
            receipt.CampaignOrFundName,
            receipt.DocumentUrl,
            receipt.DeliveryStatus.ToString());

    private static ReceiptRegisterRowResponse ToRegisterRow(DonationIntent intent, bool canSeeSensitiveDonor) =>
        new(intent.Id,

            // NO RECEIPT NUMBER, AND THAT IS THE POINT. Nothing was received, so there is no tax
            // document and nothing for a donor to claim on. The row quotes the donation reference
            // instead, which is what support asks for.
            null,

            intent.IntentReference,
            intent.LastAttemptAtUtc ?? intent.CreatedAtUtc,
            intent.DonorName,
            PaymentMappingConfig.ToResponse(intent.Amount),
            "Failed",
            null,
            null,
            "Not sent");

    private IReadOnlyList<string> PermittedRegisterActions()
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.ReceiptsView))
        {
            actions.Add("View");
        }

        if (currentUser.HasPermission(PermissionCodes.ReceiptsResend))
        {
            actions.Add("Resend");
        }

        if (currentUser.HasPermission(PermissionCodes.ReceiptsExport))
        {
            actions.Add("Export");
        }

        return actions;
    }
}
