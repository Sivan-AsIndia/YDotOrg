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
    IPeopleDirectory people,
    IGeographyDirectory geography,
    IFinancialDirectory financial,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : ICampaignReadService
{
    private static readonly IReadOnlyDictionary<Guid, PersonSummary> NoPeople =
        new Dictionary<Guid, PersonSummary>();

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

                // THE IDS, NOT JUST THE COUNT. The register needs to name the owners, and the
                // entity's own Owners collection is empty here - this query does not Include it -
                // so anything read from `campaign.Owners` downstream is silently zero or blank.
                OwnerIds = campaign.Owners
                    .OrderByDescending(owner => owner.IsPrimary)
                    .Select(owner => owner.OwnerId)
                    .ToList(),

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

        // ONE DIRECTORY CALL AND ONE CURRENCY CALL FOR THE WHOLE PAGE, not one per row. Twenty
        // campaigns reference a handful of owners and one or two currencies between them; asking
        // per row is forty queries to draw one grid, and it only shows up under load.
        var resolvedPeople = await ResolvePeopleAsync(
            [.. rows.SelectMany(row => row.OwnerIds)], cancellationToken);

        var currencyCodes = await financial.GetCurrencyCodesAsync(
            [.. rows.Select(row => row.Campaign.CurrencyId).Distinct()], cancellationToken);

        var items = rows
            .Select(row => row.Campaign.ToListItemResponse(
                today,
                row.OwnerIds,
                resolvedPeople,
                currencyCodes.GetValueOrDefault(row.Campaign.CurrencyId),
                row.TrackingAssetCount,
                row.OutstandingCheckCount))
            .ToList();

        return new PagedResponse<CampaignListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<CampaignDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // THE CHANNEL ROW IS INCLUDED THROUGH THE JOIN, not just the join itself. Without
        // ThenInclude, campaign.Channels holds ids and no names - which is what left the detail
        // screen's Channel row reading "-" on a campaign that ran on three of them.
        var campaign = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
            .Include(entity => entity.Owners)
            .Include(entity => entity.Channels)
                .ThenInclude(link => link.Channel)
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

        var outstandingCheckCount = await context.CampaignReadinessChecks
            .AsNoTracking()
            .Where(check => check.CampaignId == id)
            .Where(check => check.RequiredForLaunch)
            .CountAsync(
                check => check.Status != ReadinessCheckStatus.Passed
                         || check.Blockers.Any(blocker => !blocker.IsResolved),
                cancellationToken);

        var trackingAssetCount = await context.TrackingAssets
            .AsNoTracking()
            .CountAsync(asset => asset.CampaignId == id, cancellationToken);

        // ---- The names the screen actually prints ----------------------------------------------
        //
        // The detail response used to carry only ids: a currency id, three geography ids and a
        // list of channel ids. Everything downstream of them - Currency, Location, Channel - drew
        // a dash, because nothing the client held could turn a Guid into a word. Resolving them
        // here is one extra round trip per detail load and removes four from the client.
        var resolvedPeople = await ResolvePeopleAsync(
            [.. campaign.Owners.Select(owner => owner.OwnerId)], cancellationToken);

        var place = await geography.GetPlaceNamesAsync(
            campaign.CountryId, campaign.StateId, campaign.CityId, cancellationToken);

        var currencyCodes = await financial.GetCurrencyCodesAsync(
            [campaign.CurrencyId], cancellationToken);

        return campaign.ToDetailResponse(
            pendingClose,
            CampaignMappingConfig.PermittedActionsFor(
                campaign,
                currentUser.UserId,
                currentUser.HasPermission,
                outstandingCheckCount > 0,
                pendingClose is not null),
            resolvedPeople,
            place,
            currencyCodes.GetValueOrDefault(campaign.CurrencyId),
            trackingAssetCount,
            outstandingCheckCount);
    }

    /// <summary>
    /// Display names for a set of owner ids.
    ///
    /// A NAME IS DECORATION, so this never fails a page: an unresolvable id, or no Organisation
    /// on the request at all, produces an empty dictionary and the screen prints the id.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, PersonSummary>> ResolvePeopleAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var wanted = userIds.Where(id => id != Guid.Empty).Distinct().ToArray();

        return wanted.Length == 0 || !tenantContext.HasTenant
            ? NoPeople
            : await people.GetPeopleAsync(tenantContext.RequireTenantId(), wanted, cancellationToken);
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
    /// they see - which, for somebody whose data scope is "own records", is the campaigns they
    /// created or are named an owner of. OWNERSHIP IS NOT A ROLE: it is a row in
    /// cam_campaign_owners, and it decides what a caller SEES while their role decides what they
    /// may DO.
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
