using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Mappings;
using YDot.PAY.Application.Features.Receipts.Mappings;
using YDot.PAY.Application.Features.Refunds.Mappings;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The read side of the donation register.
///
/// THE THREE CASE COLLECTIONS ARE INCLUDED ON EVERY READ, which looks wasteful and is not:
/// <c>HasIssuedReceipt</c>, <c>HasOpenCase</c> and the whole permitted-action list are computed
/// from them. Loading a donation without them would report a receipted donation as unreceipted
/// and offer to issue a second one - a duplicate tax document, which is the single worst thing
/// this module can produce.
/// </summary>
public sealed class DonationReadService(
    PaymentDbContext context,
    ICurrentUser currentUser,
    ICampaignDirectory campaigns,
    IDateTimeProvider clock)
    : IDonationReadService
{
    public async Task<PagedResponse<DonationListItemResponse>> SearchAsync(
        DonationSearchFilter filter,
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

        var campaignNames = await ResolveCampaignNamesAsync(
            scope.TenantId, rows.Select(donation => donation.CampaignId), cancellationToken);

        var items = rows
            .Select(donation => donation.ToListItemResponse(
                LookupCampaign(campaignNames, donation.CampaignId),
                CurrentReceiptNumber(donation),
                canSeeSensitiveDonor))
            .ToList();

        return new PagedResponse<DonationListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<DonationDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var donation = await BaseQuery()
            .Include(candidate => candidate.DonationIntent)
            .Include(candidate => candidate.Receipts)
                .ThenInclude(receipt => receipt.Deliveries)
            .Where(candidate => candidate.Id == id)
            .Where(candidate => !scope.IsOwnRecordsOnly || candidate.CreatedByUserId == scope.UserId)

            // A DONOR READS THEIR OWN DONATION AND NO OTHER - see the note on the list filter.
            .Where(candidate => !scope.IsDonorSelfService
                                || (scope.HasDonorIdentity
                                    && candidate.DonorEmail.ToLower() == scope.DonorEmail))
            .FirstOrDefaultAsync(cancellationToken);

        if (donation is null)
        {
            return null;
        }

        var campaignName = donation.CampaignId.HasValue
            ? await campaigns.GetCampaignNameAsync(
                donation.TenantId, donation.CampaignId.Value, cancellationToken)
            : null;

        var now = clock.UtcNow;

        return new DonationDetailResponse(
            donation.Id,
            donation.TenantId,
            donation.DonationReference,
            donation.DonationIntentId,
            donation.DonationIntent?.IntentReference ?? string.Empty,
            donation.PaymentAttemptId,
            donation.DonorId,
            donation.CampaignId,
            campaignName,
            donation.Amount.ToResponse(),
            donation.GatewayFee.ToResponseOrNull(),
            donation.NetAmount.ToResponseOrNull(),
            donation.RefundedAmount.ToResponse(),
            donation.RefundableAmount.ToResponse(),
            donation.DonorName,
            PaymentMappingConfig.MaskEmail(donation.DonorEmail, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskMobile(donation.DonorMobile, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskTaxIdentifier(donation.DonorTaxIdentifier, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskAddress(donation.DonorAddress, canSeeSensitiveDonor),
            donation.Status,
            PaymentMappingConfig.Describe(donation.Status),
            donation.DonatedAtUtc,
            donation.MethodType,
            donation.GatewayReference,
            donation.SettlementStatus,
            donation.SettledAtUtc,
            donation.SettlementBatchReference,
            donation.ReconciliationStatus,
            donation.ReconciledAtUtc,
            donation.ReconciliationNote,
            donation.SourceType,
            PaymentMappingConfig.Describe(donation.SourceType),
            donation.TrackingAssetId,
            donation.LeadId,
            donation.IsReceiptable,

            // Newest version first: a corrected receipt is what somebody looking at the donation
            // today actually cares about, with the superseded ones underneath it.
            [.. donation.Receipts
                .OrderByDescending(receipt => receipt.VersionNumber)
                .Select(receipt => receipt.ToSummaryResponse())],

            [.. donation.RefundCases
                .OrderByDescending(refundCase => refundCase.RequestedAtUtc)
                .Select(refundCase => refundCase.ToSummaryResponse())],

            [.. donation.ChargebackCases
                .OrderByDescending(chargeback => chargeback.OpenedAtUtc)
                .Select(chargeback => chargeback.ToSummaryResponse(now))],

            donation.CreatedAtUtc,
            donation.CreatedByUserId,
            donation.UpdatedAtUtc,
            donation.UpdatedByUserId,
            donation.Version,
            DonationMappingConfig.PermittedActionsFor(donation, currentUser.HasPermission));
    }

    /// <summary>
    /// Counts and totals for the register tiles.
    ///
    /// THE TOTALS ARE GROUPED BY CURRENCY AND THEN NARROWED TO THE PREDOMINANT ONE, rather than
    /// summed across the lot. Adding a rupee to a dollar is the one arithmetic a money total must
    /// never do, and a tile reading "1,250,000" made of three currencies is worse than no tile:
    /// it looks authoritative and is meaningless.
    ///
    /// The counts, unlike the totals, are currency-agnostic and cover everything.
    /// </summary>
    public async Task<DonationStatisticsResponse> GetStatisticsAsync(
        AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var query = context.Donations.AsNoTracking();

        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(donation => donation.CreatedByUserId == scope.UserId);
        }

        // THE STATISTICS ARE SCOPED TOO. A donor's totals must be their own giving, not the
        // Organisation's - a summary card is a disclosure like any other row.
        query = ApplyDonorScope(query, scope);

        var counts = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalCount = group.Count(),
                RecordedCount = group.Count(donation => donation.Status == DonationStatus.Recorded),
                SettledCount = group.Count(donation => donation.Status == DonationStatus.Settled),
                RefundedCount = group.Count(donation =>
                    donation.Status == DonationStatus.Refunded
                    || donation.Status == DonationStatus.PartiallyRefunded),
                ChargedBackCount = group.Count(donation => donation.Status == DonationStatus.ChargedBack),
                UnreconciledCount = group.Count(donation =>
                    donation.ReconciliationStatus == ReconciliationStatus.Unreconciled)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Donations with no currently valid receipt, which is the queue the receipt register
        // works from. Expressed against the receipt table rather than a flag, because a voided
        // receipt puts a donation back into this queue and a flag would not.
        var awaitingReceiptCount = await query
            .Where(donation => donation.Status == DonationStatus.Recorded
                               || donation.Status == DonationStatus.Settled
                               || donation.Status == DonationStatus.PartiallyRefunded)
            .Where(donation => !donation.Receipts.Any(receipt => receipt.Status == ReceiptStatus.Issued))
            .CountAsync(cancellationToken);

        var byCurrency = await query
            .GroupBy(donation => donation.Amount.CurrencyCode)
            .Select(group => new
            {
                CurrencyCode = group.Key,
                Count = group.Count(),
                Total = group.Sum(donation => donation.Amount.Amount),
                Refunded = group.Sum(donation => donation.RefundedAmount.Amount)
            })
            .ToListAsync(cancellationToken);

        // The currency most of the money is in. An Organisation with a single currency - which is
        // nearly all of them - gets exactly what it expects; one with several gets its main
        // figure rather than a nonsense sum.
        var predominant = byCurrency
            .OrderByDescending(row => row.Count)
            .FirstOrDefault();

        var currencyCode = predominant?.CurrencyCode ?? "INR";
        var total = predominant?.Total ?? 0m;
        var refunded = predominant?.Refunded ?? 0m;

        return new DonationStatisticsResponse(
            counts?.TotalCount ?? 0,
            MoneyResponse.Plain(total, currencyCode),
            MoneyResponse.Plain(refunded, currencyCode),
            MoneyResponse.Plain(total - refunded, currencyCode),
            counts?.RecordedCount ?? 0,
            counts?.SettledCount ?? 0,
            counts?.RefundedCount ?? 0,
            counts?.ChargedBackCount ?? 0,
            awaitingReceiptCount,
            counts?.UnreconciledCount ?? 0);
    }

    /// <summary>
    /// The export rows.
    ///
    /// IT IS CAPPED, and the cap is not negotiable: an unbounded export of a busy Organisation's
    /// donation history is both a memory problem and a data-protection one. The caller's filter
    /// narrows it; the cap stops an empty filter taking everything.
    /// </summary>
    public async Task<IReadOnlyList<DonationExportRow>> GetExportRowsAsync(
        DonationSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        const int MaximumExportRows = 50_000;

        var rows = await ApplySort(ApplyFilter(BaseQuery(), filter, scope), filter.Sort)
            .Include(donation => donation.DonationIntent)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        var campaignNames = await ResolveCampaignNamesAsync(
            scope.TenantId, rows.Select(donation => donation.CampaignId), cancellationToken);

        return [.. rows.Select(donation => donation.ToExportRow(
            donation.DonationIntent?.IntentReference ?? string.Empty,
            LookupCampaign(campaignNames, donation.CampaignId),
            CurrentReceiptNumber(donation),
            canSeeSensitiveDonor))];
    }

    // =====================================================================================
    // Shared shaping
    // =====================================================================================

    private IQueryable<Donation> BaseQuery() =>
        context.Donations
            .AsNoTracking()
            .Include(donation => donation.Receipts)
            .Include(donation => donation.RefundCases)
            .Include(donation => donation.ChargebackCases);

    private static string? CurrentReceiptNumber(Donation donation) =>
        donation.Receipts
            .Where(receipt => receipt.Status == ReceiptStatus.Issued)
            .OrderByDescending(receipt => receipt.VersionNumber)
            .Select(receipt => receipt.ReceiptNumber)
            .FirstOrDefault();

    /// <summary>
    /// Narrows a query to the signed-in donor's own donations.
    ///
    /// <c>Donation.DonorEmail</c> IS A SNAPSHOT OF WHAT THE DONOR TYPED and has no normalised
    /// twin, so the comparison lowers the column. It is the correct source even so: the donation
    /// is deliberately a snapshot of the gift as it was made, which is what a receipt must show
    /// years later.
    ///
    /// NO IDENTITY MEANS NO ROWS, never all rows.
    /// </summary>
    private static IQueryable<Donation> ApplyDonorScope(
        IQueryable<Donation> query, AccessScope scope)
    {
        if (!scope.IsDonorSelfService)
        {
            return query;
        }

        return scope.HasDonorIdentity
            ? query.Where(donation => donation.DonorEmail.ToLower() == scope.DonorEmail)
            : query.Where(_ => false);
    }

    private static IQueryable<Donation> ApplyFilter(
        IQueryable<Donation> query, DonationSearchFilter filter, AccessScope scope)
    {
        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(donation => donation.CreatedByUserId == scope.UserId);
        }

        query = ApplyDonorScope(query, scope);

        if (filter.Status.HasValue)
        {
            query = query.Where(donation => donation.Status == filter.Status.Value);
        }

        if (filter.SettlementStatus.HasValue)
        {
            query = query.Where(donation => donation.SettlementStatus == filter.SettlementStatus.Value);
        }

        if (filter.ReconciliationStatus.HasValue)
        {
            query = query.Where(donation =>
                donation.ReconciliationStatus == filter.ReconciliationStatus.Value);
        }

        if (filter.CampaignId.HasValue)
        {
            query = query.Where(donation => donation.CampaignId == filter.CampaignId.Value);
        }

        if (filter.DonorId.HasValue)
        {
            query = query.Where(donation => donation.DonorId == filter.DonorId.Value);
        }

        if (filter.SourceType.HasValue)
        {
            query = query.Where(donation => donation.SourceType == filter.SourceType.Value);
        }

        if (filter.MethodType.HasValue)
        {
            query = query.Where(donation => donation.MethodType == filter.MethodType.Value);
        }

        if (filter.DonatedFromUtc.HasValue)
        {
            query = query.Where(donation => donation.DonatedAtUtc >= filter.DonatedFromUtc.Value);
        }

        if (filter.DonatedToUtc.HasValue)
        {
            query = query.Where(donation => donation.DonatedAtUtc <= filter.DonatedToUtc.Value);
        }

        if (filter.MinimumAmount.HasValue)
        {
            query = query.Where(donation => donation.Amount.Amount >= filter.MinimumAmount.Value);
        }

        if (filter.MaximumAmount.HasValue)
        {
            query = query.Where(donation => donation.Amount.Amount <= filter.MaximumAmount.Value);
        }

        // Expressed against the receipts themselves rather than a denormalised flag, so voiding
        // a receipt puts the donation back into the queue automatically.
        if (filter.AwaitingReceipt == true)
        {
            query = query.Where(donation =>
                !donation.Receipts.Any(receipt => receipt.Status == ReceiptStatus.Issued));
        }
        else if (filter.AwaitingReceipt == false)
        {
            query = query.Where(donation =>
                donation.Receipts.Any(receipt => receipt.Status == ReceiptStatus.Issued));
        }

        if (filter.HasOpenCase.HasValue)
        {
            // The open-case states are written out rather than calling the entity's IsOpen,
            // because a computed property has no SQL translation - EF would fall back to
            // evaluating it in memory over every donation the Organisation owns.
            var wantsOpen = filter.HasOpenCase.Value;

            query = query.Where(donation =>
                (donation.RefundCases.Any(refundCase =>
                     refundCase.Status == RefundStatus.Requested
                     || refundCase.Status == RefundStatus.Approved
                     || refundCase.Status == RefundStatus.Processing)
                 || donation.ChargebackCases.Any(chargeback =>
                     chargeback.Status == ChargebackStatus.Opened
                     || chargeback.Status == ChargebackStatus.EvidenceRequired
                     || chargeback.Status == ChargebackStatus.UnderReview)) == wantsOpen);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(donation =>
                donation.DonationReference.ToLower().Contains(term)
                || donation.DonorName.ToLower().Contains(term)
                || donation.DonorEmail.ToLower().Contains(term)
                || (donation.GatewayReference != null && donation.GatewayReference.ToLower().Contains(term)));
        }

        return query;
    }

    private static IQueryable<Donation> ApplySort(IQueryable<Donation> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "reference" => query.OrderBy(donation => donation.DonationReference),
            "reference_desc" => query.OrderByDescending(donation => donation.DonationReference),
            "donor" => query.OrderBy(donation => donation.DonorName),
            "donor_desc" => query.OrderByDescending(donation => donation.DonorName),
            "amount" => query.OrderBy(donation => donation.Amount.Amount),
            "amount_desc" => query.OrderByDescending(donation => donation.Amount.Amount),
            "status" => query.OrderBy(donation => donation.Status)
                .ThenByDescending(donation => donation.DonatedAtUtc),
            "date" => query.OrderBy(donation => donation.DonatedAtUtc),
            _ => query.OrderByDescending(donation => donation.DonatedAtUtc)
        };

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveCampaignNamesAsync(
        Guid tenantId, IEnumerable<Guid?> campaignIds, CancellationToken cancellationToken)
    {
        var ids = campaignIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await campaigns.GetCampaignNamesAsync(tenantId, ids, cancellationToken);
    }

    private static string? LookupCampaign(IReadOnlyDictionary<Guid, string> names, Guid? campaignId) =>
        campaignId.HasValue && names.TryGetValue(campaignId.Value, out var name) ? name : null;
}
