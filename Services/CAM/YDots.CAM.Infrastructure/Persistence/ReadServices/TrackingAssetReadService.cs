using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;
using YDots.CAM.Application.Features.TrackingAssets.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the tracking asset manager.
///
/// THE FOUR NAMES EACH ROW SHOWS - campaign, channel, source, medium - ARE JOINED IN THE
/// PROJECTION rather than fetched per row afterwards. A grid of twenty assets is one query this
/// way and eighty-one the other, and the eighty-one only shows up under a load test.
/// </summary>
public sealed class TrackingAssetReadService(
    CampaignDbContext context,
    IFinancialDirectory financial,
    IGeographyDirectory geography,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : ITrackingAssetReadService
{
    private static readonly IReadOnlyDictionary<Guid, CampaignIncome> NoIncome =
        new Dictionary<Guid, CampaignIncome>();

    private static readonly IReadOnlyDictionary<Guid, string> NoNames = new Dictionary<Guid, string>();

    public async Task<PagedResponse<TrackingAssetListItemResponse>> SearchAsync(
        TrackingAssetSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var now = clock.UtcNow;

        var query = ApplyScope(context.TrackingAssets.AsNoTracking(), scope);
        query = ApplyFilter(query, filter, now);

        var total = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Include(asset => asset.Places)
            .Select(asset => new
            {
                Asset = asset,
                CampaignCode = asset.Campaign.Code,
                CampaignName = asset.Campaign.Name,

                // Joined by id rather than through a navigation property, because a tracking
                // asset references the three reference tables without owning a navigation to
                // them - see the entity for why those are loose references.
                ChannelName = context.Channels
                    .Where(channel => channel.Id == asset.ChannelId)
                    .Select(channel => channel.Name)
                    .FirstOrDefault() ?? string.Empty,

                SourceName = context.Sources
                    .Where(source => source.Id == asset.SourceId)
                    .Select(source => source.Name)
                    .FirstOrDefault() ?? string.Empty,

                MediumName = context.Mediums
                    .Where(medium => medium.Id == asset.MediumId)
                    .Select(medium => medium.Name)
                    .FirstOrDefault() ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        // ONE INCOME QUERY FOR THE WHOLE PAGE. The "Total usage" and collected-amount tiles on
        // the manager read from the donations, and the alternative is a query per row on the
        // screen people open the module with.
        var income = await ResolveIncomeAsync(
            [.. rows.Select(row => row.Asset.Id)], cancellationToken);

        var items = rows
            .Select(row => row.Asset.ToListItemResponse(
                row.CampaignCode, row.CampaignName,
                row.ChannelName, row.SourceName, row.MediumName, now,
                income.GetValueOrDefault(row.Asset.Id)))
            .ToList();

        return new PagedResponse<TrackingAssetListItemResponse>(
            items, total, filter.Page, filter.PageSize);
    }

    public async Task<TrackingAssetDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var row = await ApplyScope(context.TrackingAssets.AsNoTracking(), scope)
            .Where(asset => asset.Id == id)
            .Include(asset => asset.Places)
                .ThenInclude(place => place.CustomFields)
            .Select(asset => new
            {
                Asset = asset,
                CampaignCode = asset.Campaign.Code,
                CampaignName = asset.Campaign.Name,
                ChannelName = context.Channels
                    .Where(channel => channel.Id == asset.ChannelId)
                    .Select(channel => channel.Name).FirstOrDefault() ?? string.Empty,
                SourceName = context.Sources
                    .Where(source => source.Id == asset.SourceId)
                    .Select(source => source.Name).FirstOrDefault() ?? string.Empty,
                MediumName = context.Mediums
                    .Where(medium => medium.Id == asset.MediumId)
                    .Select(medium => medium.Name).FirstOrDefault() ?? string.Empty
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var income = await ResolveIncomeAsync([row.Asset.Id], cancellationToken);

        // The placements carry city and state ids taken from the campaign. Naming them is what
        // turns "Places: 3" on the asset popup into three lines somebody can read.
        var cityIds = row.Asset.Places
            .Where(place => place.CityId.HasValue)
            .Select(place => place.CityId!.Value)
            .ToArray();

        var stateIds = row.Asset.Places
            .Where(place => place.StateId.HasValue)
            .Select(place => place.StateId!.Value)
            .ToArray();

        var cityNames = cityIds.Length == 0
            ? NoNames
            : await geography.GetCityNamesAsync(cityIds, cancellationToken);

        var stateNames = stateIds.Length == 0
            ? NoNames
            : await geography.GetStateNamesAsync(stateIds, cancellationToken);

        return row.Asset.ToDetailResponse(
            row.CampaignCode, row.CampaignName,
            row.ChannelName, row.SourceName, row.MediumName,
            clock.UtcNow,
            TrackingAssetMappingConfig.PermittedActionsFor(
                row.Asset, currentUser.UserId, currentUser.HasPermission),
            income.GetValueOrDefault(row.Asset.Id),
            cityNames,
            stateNames);
    }

    /// <summary>
    /// What a set of assets has raised, keyed by asset id.
    ///
    /// NEVER THROWS AND NEVER BLOCKS THE PAGE: with no Organisation resolved, or nothing to ask
    /// about, it answers empty and every figure renders as zero. The financial directory itself
    /// swallows a database failure for the same reason - a manager that will not open tells
    /// nobody anything.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, CampaignIncome>> ResolveIncomeAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0 || !tenantContext.HasTenant)
        {
            return NoIncome;
        }

        return await financial.GetTrackingAssetIncomeAsync(
            tenantContext.RequireTenantId(), assetIds, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackingAssetExportRow>> GetExportRowsAsync(
        TrackingAssetSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyScope(context.TrackingAssets.AsNoTracking(), scope);
        query = ApplyFilter(query, filter, clock.UtcNow);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(asset => new
            {
                Asset = asset,
                CampaignCode = asset.Campaign.Code,
                ChannelName = context.Channels
                    .Where(channel => channel.Id == asset.ChannelId)
                    .Select(channel => channel.Name).FirstOrDefault() ?? string.Empty,
                SourceName = context.Sources
                    .Where(source => source.Id == asset.SourceId)
                    .Select(source => source.Name).FirstOrDefault() ?? string.Empty,
                MediumName = context.Mediums
                    .Where(medium => medium.Id == asset.MediumId)
                    .Select(medium => medium.Name).FirstOrDefault() ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var income = await ResolveIncomeAsync(
            [.. rows.Select(row => row.Asset.Id)], cancellationToken);

        return [.. rows.Select(row => row.Asset.ToExportRow(
            row.CampaignCode, row.ChannelName, row.SourceName, row.MediumName,
            income.GetValueOrDefault(row.Asset.Id)))];
    }

    /// <summary>
    /// Narrows to assets on the caller's own campaigns when their data scope says so.
    ///
    /// Scoped through the CAMPAIGN rather than through the asset's own creator, because a
    /// tracking asset belongs to whoever owns the campaign it promotes - somebody who owns a
    /// campaign should see every asset on it, including ones a colleague created.
    /// </summary>
    private static IQueryable<TrackingAsset> ApplyScope(
        IQueryable<TrackingAsset> query, AccessScope scope) =>
        scope.IsOwnRecordsOnly
            ? query.Where(asset =>
                asset.CreatedByUserId == scope.UserId
                || asset.Campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            : query;

    private static IQueryable<TrackingAsset> ApplyFilter(
        IQueryable<TrackingAsset> query, TrackingAssetSearchFilter filter, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(asset =>
                asset.Code.ToLower().Contains(term)
                || asset.Destination.ToLower().Contains(term)
                || (asset.TrackingReference != null && asset.TrackingReference.ToLower().Contains(term))
                || (asset.ContentTag != null && asset.ContentTag.ToLower().Contains(term)));
        }

        if (filter.CampaignId.HasValue)
        {
            query = query.Where(asset => asset.CampaignId == filter.CampaignId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(asset => asset.Status == filter.Status.Value);
        }

        if (filter.AssetType.HasValue)
        {
            query = query.Where(asset => asset.AssetType == filter.AssetType.Value);
        }

        if (filter.ChannelId.HasValue)
        {
            query = query.Where(asset => asset.ChannelId == filter.ChannelId.Value);
        }

        if (filter.SourceId.HasValue)
        {
            query = query.Where(asset => asset.SourceId == filter.SourceId.Value);
        }

        if (filter.MediumId.HasValue)
        {
            query = query.Where(asset => asset.MediumId == filter.MediumId.Value);
        }

        // Active AND inside its own window. An Active asset whose window closed last week is
        // precisely the row somebody is looking for when a QR code has stopped working, and a
        // Status filter alone would hide it among the ones that are working fine.
        if (filter.IsLiveNow == true)
        {
            query = query.Where(asset =>
                asset.Status == TrackingAssetStatus.Active
                && asset.ActiveFrom <= now
                && asset.ActiveTo >= now);
        }
        else if (filter.IsLiveNow == false)
        {
            query = query.Where(asset =>
                asset.Status != TrackingAssetStatus.Active
                || asset.ActiveFrom > now
                || asset.ActiveTo < now);
        }

        return query;
    }

    private static IQueryable<TrackingAsset> ApplySort(IQueryable<TrackingAsset> query, string? sort)
    {
        var descending = sort?.EndsWith(" desc", StringComparison.OrdinalIgnoreCase) == true;

        var field = sort?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?.ToLowerInvariant();

        Expression<Func<TrackingAsset, object>> key = field switch
        {
            "code" => asset => asset.Code,
            "status" => asset => asset.Status,
            "assettype" => asset => asset.AssetType,
            "activefrom" => asset => asset.ActiveFrom,
            "activeto" => asset => asset.ActiveTo,
            "usagecount" => asset => asset.UsageCount,
            "totalreceived" => asset => asset.TotalReceived,
            "createdatutc" => asset => asset.CreatedAtUtc,
            _ => asset => asset.UpdatedAtUtc ?? asset.CreatedAtUtc
        };

        var ordered = field is null || descending
            ? query.OrderByDescending(key)
            : query.OrderBy(key);

        return ordered.ThenBy(asset => asset.Id);
    }
}
