using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Application.Features.Campaigns.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the campaign register, the detail screen and the history tab.
///
/// THE ORGANISATION FILTER IS ALREADY APPLIED UNDERNEATH, so nothing here has to remember it
/// and nothing here can reach past it. <see cref="AccessScope"/> only ever NARROWS within one
/// Organisation - to the caller's own campaigns, for a role scoped that way.
///
/// COUNTS ARE PROJECTED, NOT FETCHED PER ROW. A register showing "4 owners, 12 assets, 2 checks
/// outstanding" for twenty campaigns is one query with correlated subqueries, not sixty-one
/// queries - EF turns <c>campaign.Owners.Count()</c> inside a Select into exactly that.
/// </summary>
public sealed class CampaignReadService(
    CampaignDbContext context,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ICampaignReadService
{
    public async Task<PagedResponse<CampaignListItemResponse>> SearchAsync(
        CampaignSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyScope(context.Campaigns.AsNoTracking(), scope);
        query = ApplyFilter(query, filter, clock.TodayUtc);

        var total = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(campaign => new
            {
                Campaign = campaign,
                OwnerCount = campaign.Owners.Count(),
                TrackingAssetCount = campaign.TrackingAssets.Count(),

                // The same predicate the launch gate uses, so the number the register shows and
                // the number that blocks activation can never disagree.
                OutstandingCheckCount = campaign.ReadinessChecks.Count(check =>
                    check.RequiredForLaunch
                    && (check.Status != ReadinessCheckStatus.Passed
                        || check.Blockers.Any(blocker => !blocker.IsResolved)))
            })
            .ToListAsync(cancellationToken);

        var today = clock.TodayUtc;

        var items = rows
            .Select(row => row.Campaign.ToListItemResponse(
                today, row.TrackingAssetCount, row.OutstandingCheckCount))
            .ToList();

        return new PagedResponse<CampaignListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<CampaignDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var campaign = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
            .Include(entity => entity.Owners)
            .Include(entity => entity.Channels)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (campaign is null)
        {
            return null;
        }

        var pendingClose = await context.CampaignLifecycleActions
            .AsNoTracking()
            .Where(action => action.CampaignId == id)
            .Where(action => action.ActionType == CampaignLifecycleActionType.RequestClose)
            .Where(action => action.ActionStatus == CampaignLifecycleActionStatus.Pending)
            .OrderByDescending(action => action.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var hasOutstandingChecks = await context.CampaignReadinessChecks
            .AsNoTracking()
            .Where(check => check.CampaignId == id)
            .Where(check => check.RequiredForLaunch)
            .AnyAsync(
                check => check.Status != ReadinessCheckStatus.Passed
                         || check.Blockers.Any(blocker => !blocker.IsResolved),
                cancellationToken);

        return campaign.ToDetailResponse(
            pendingClose,
            CampaignMappingConfig.PermittedActionsFor(
                campaign,
                currentUser.UserId,
                currentUser.HasPermission,
                hasOutstandingChecks,
                pendingClose is not null));
    }

    public async Task<PagedResponse<CampaignHistoryResponse>> GetHistoryAsync(
        Guid campaignId, PaginationRequest pagination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pagination);

        // THE AUDIT TABLE IS NOT ORGANISATION-FILTERED - see CampaignAuditEvent for why - so
        // this narrows it explicitly. The CALLER's right to see this campaign was already
        // established by the query handler, which resolves the campaign through the filter
        // before ever reaching here.
        var query = context.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.TargetId == campaignId);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(audit => audit.ToHistoryResponse()).ToList();

        return new PagedResponse<CampaignHistoryResponse>(
            items, total, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    /// Counts by status.
    ///
    /// ONE GROUPED QUERY rather than nine counts. The alternative reads the same table nine
    /// times to answer one question, and the tiles are on the register's first paint.
    /// </summary>
    public async Task<CampaignStatisticsResponse> GetStatisticsAsync(
        AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var counts = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
            .GroupBy(campaign => campaign.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int For(CampaignStatus status) =>
            counts.FirstOrDefault(entry => entry.Status == status)?.Count ?? 0;

        return new CampaignStatisticsResponse(
            counts.Sum(entry => entry.Count),
            For(CampaignStatus.Draft),
            For(CampaignStatus.Submitted),
            For(CampaignStatus.Approved),
            For(CampaignStatus.Scheduled),
            For(CampaignStatus.Active),
            For(CampaignStatus.Paused),
            For(CampaignStatus.Closing),
            For(CampaignStatus.Closed),
            For(CampaignStatus.Cancelled));
    }

    public async Task<IReadOnlyList<CampaignExportRow>> GetExportRowsAsync(
        CampaignSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyScope(context.Campaigns.AsNoTracking(), scope);
        query = ApplyFilter(query, filter, clock.TodayUtc);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Include(campaign => campaign.Owners)
            .Select(campaign => new
            {
                Campaign = campaign,
                TrackingAssetCount = campaign.TrackingAssets.Count()
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => row.Campaign.ToExportRow(row.TrackingAssetCount))];
    }

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(
        string? search, int take, CancellationToken cancellationToken)
    {
        var query = context.Campaigns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(campaign =>
                campaign.Name.ToLower().Contains(term) || campaign.Code.ToLower().Contains(term));
        }

        // A picker offers what can be USED. A Closed or Cancelled campaign cannot take a new
        // tracking asset, so offering it would produce a selection the next call refuses.
        var rows = await query
            .Where(campaign => campaign.Status != CampaignStatus.Closed
                               && campaign.Status != CampaignStatus.Cancelled)
            .OrderBy(campaign => campaign.Name)
            .Take(Math.Clamp(take, 1, 200))
            .Select(campaign => new { campaign.Id, campaign.Code, campaign.Name, campaign.Status })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new LookupItem(
            row.Id, row.Code, row.Name, row.Status == CampaignStatus.Active))];
    }

    // =====================================================================================
    // Query helpers
    // =====================================================================================

    /// <summary>
    /// Narrows to the caller's own campaigns when their data scope says so.
    ///
    /// THIS IS NOT THE ORGANISATION BOUNDARY. That is enforced underneath by the query filter
    /// and cannot be widened from here. This decides how much of the caller's OWN Organisation
    /// they see - which for a Campaign Owner scoped to "own" is the campaigns they own.
    /// </summary>
    private static IQueryable<Campaign> ApplyScope(IQueryable<Campaign> query, AccessScope scope) =>
        scope.IsOwnRecordsOnly
            ? query.Where(campaign =>
                campaign.CreatedByUserId == scope.UserId
                || campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            : query;

    private static IQueryable<Campaign> ApplyFilter(
        IQueryable<Campaign> query, CampaignSearchFilter filter, DateOnly today)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(campaign =>
                campaign.Name.ToLower().Contains(term)
                || campaign.Code.ToLower().Contains(term)
                || campaign.FundOrProgramme.ToLower().Contains(term)
                || campaign.Purpose.ToLower().Contains(term));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(campaign => campaign.Status == filter.Status.Value);
        }

        if (filter.CurrencyId.HasValue)
        {
            query = query.Where(campaign => campaign.CurrencyId == filter.CurrencyId.Value);
        }

        if (filter.CountryId.HasValue)
        {
            query = query.Where(campaign => campaign.CountryId == filter.CountryId.Value);
        }

        if (filter.OwnerId.HasValue)
        {
            query = query.Where(campaign =>
                campaign.Owners.Any(owner => owner.OwnerId == filter.OwnerId.Value));
        }

        if (filter.StartsOnOrAfter.HasValue)
        {
            query = query.Where(campaign => campaign.StartDate >= filter.StartsOnOrAfter.Value);
        }

        if (filter.EndsOnOrBefore.HasValue)
        {
            query = query.Where(campaign => campaign.EndDate <= filter.EndsOnOrBefore.Value);
        }

        // "Running now" is Active AND inside its own dates, which is a different question from
        // the Active status alone - an Active campaign whose end date has passed is exactly the
        // row somebody is looking for when they ask which ones need closing.
        if (filter.IsRunningNow == true)
        {
            query = query.Where(campaign =>
                campaign.Status == CampaignStatus.Active
                && campaign.StartDate <= today
                && campaign.EndDate >= today);
        }
        else if (filter.IsRunningNow == false)
        {
            query = query.Where(campaign =>
                campaign.Status != CampaignStatus.Active
                || campaign.StartDate > today
                || campaign.EndDate < today);
        }

        return query;
    }

    /// <summary>
    /// The register's sort.
    ///
    /// An unrecognised expression falls back to the default rather than throwing: a bad query
    /// string should not turn a list into a 500.
    /// </summary>
    private static IQueryable<Campaign> ApplySort(IQueryable<Campaign> query, string? sort)
    {
        var descending = sort?.EndsWith(" desc", StringComparison.OrdinalIgnoreCase) == true;

        var field = sort?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?.ToLowerInvariant();

        Expression<Func<Campaign, object>> key = field switch
        {
            "name" => campaign => campaign.Name,
            "code" => campaign => campaign.Code,
            "status" => campaign => campaign.Status,
            "startdate" => campaign => campaign.StartDate,
            "enddate" => campaign => campaign.EndDate,
            "targetamount" => campaign => campaign.TargetAmount,
            "createdatutc" => campaign => campaign.CreatedAtUtc,

            // Newest activity first, which is what a register is usually read for. Coalesced
            // because UpdatedAtUtc is null until the first edit, and a null sorts unpredictably.
            _ => campaign => campaign.UpdatedAtUtc ?? campaign.CreatedAtUtc
        };

        var ordered = field is null || descending
            ? query.OrderByDescending(key)
            : query.OrderBy(key);

        // A stable tie-break, so paging is deterministic. Without one, two campaigns sharing a
        // sort value can swap between page 1 and page 2 and a row is silently skipped.
        return ordered.ThenBy(campaign => campaign.Id);
    }
}
