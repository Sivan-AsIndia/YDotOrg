using System.Globalization;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.TrackingAssets.Mappings;

/// <summary>Manual mapping for the Tracking Assets slice.</summary>
public static class TrackingAssetMappingConfig
{
    /// <summary>
    /// Builds a new tracking asset.
    ///
    /// The CAMPAIGN IS THE LOADED ENTITY rather than the id off the request, so the asset can
    /// only be attached to a campaign the caller was actually able to read - which, under the
    /// Organisation query filter, means one of their own.
    /// </summary>
    public static TrackingAsset ToEntity(
        this CreateTrackingAssetRequest request, Campaign campaign, string code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(campaign);

        var asset = new TrackingAsset
        {
            Code = code,
            CampaignId = campaign.Id,
            AssetType = request.AssetType,
            ChannelId = request.ChannelId,
            Destination = request.Destination.Trim(),
            SourceId = request.SourceId,
            MediumId = request.MediumId,
            ContentTag = Clean(request.ContentTag),
            Status = request.Status,
            ActiveFrom = request.ActiveFrom,
            ActiveTo = request.ActiveTo
        };

        ApplyPlaces(asset, request.Places);

        return asset;
    }

    /// <summary>Applies an update in place. Only reached for a Draft asset.</summary>
    public static void ApplyTo(this UpdateTrackingAssetRequest request, TrackingAsset asset)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(asset);

        asset.AssetType = request.AssetType;
        asset.ChannelId = request.ChannelId;
        asset.Destination = request.Destination.Trim();
        asset.SourceId = request.SourceId;
        asset.MediumId = request.MediumId;
        asset.ContentTag = Clean(request.ContentTag);
        asset.ActiveFrom = request.ActiveFrom;
        asset.ActiveTo = request.ActiveTo;

        ApplyPlaces(asset, request.Places);
    }

    /// <summary>
    /// Replaces the placement list.
    ///
    /// EXISTING PLACEMENTS ARE MATCHED BY ID AND UPDATED IN PLACE rather than being removed and
    /// re-created. A placement carries its own audit columns, and rebuilding the list on every
    /// save would reset CreatedAt on rows that never changed - losing the record of when a
    /// poster was actually put up.
    /// </summary>
    private static void ApplyPlaces(TrackingAsset asset, IReadOnlyList<TrackingAssetPlaceRequest>? places)
    {
        var wanted = places ?? [];
        var keptIds = wanted.Where(place => place.Id.HasValue).Select(place => place.Id!.Value).ToHashSet();

        foreach (var removed in asset.Places.Where(place => !keptIds.Contains(place.Id)).ToList())
        {
            asset.Places.Remove(removed);
        }

        foreach (var request in wanted)
        {
            var existing = request.Id.HasValue
                ? asset.Places.FirstOrDefault(place => place.Id == request.Id.Value)
                : null;

            if (existing is null)
            {
                var created = new TrackingAssetPlace
                {
                    TrackingAssetId = asset.Id,
                    PlaceName = request.PlaceName.Trim(),
                    CityId = request.CityId,
                    StateId = request.StateId,
                    Destination = request.Destination.Trim()
                };

                ApplyCustomFields(created, request.CustomFields);
                asset.Places.Add(created);

                continue;
            }

            existing.PlaceName = request.PlaceName.Trim();
            existing.CityId = request.CityId;
            existing.StateId = request.StateId;
            existing.Destination = request.Destination.Trim();

            ApplyCustomFields(existing, request.CustomFields);
        }
    }

    /// <summary>
    /// Replaces a placement's custom fields.
    ///
    /// These ARE rebuilt wholesale, unlike the placements above, and the difference is
    /// deliberate: a custom field is a bare name/value pair on <c>BaseEntity</c> with no audit
    /// columns to lose, so matching by id would buy nothing.
    /// </summary>
    private static void ApplyCustomFields(
        TrackingAssetPlace place, IReadOnlyList<TrackingAssetCustomFieldRequest>? fields)
    {
        place.CustomFields.Clear();

        foreach (var field in fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(field.FieldName))
            {
                continue;
            }

            place.CustomFields.Add(new TrackingAssetCustomField
            {
                TrackingAssetPlaceId = place.Id,
                FieldName = field.FieldName.Trim(),
                Value = field.Value?.Trim() ?? string.Empty
            });
        }
    }

    /// <summary>
    /// One row of the manager grid.
    ///
    /// <paramref name="income"/> IS WHAT THE ASSET HAS ACTUALLY RAISED, read from the donations
    /// by the read service. The <c>TotalReceived</c> column on the row is never written by
    /// anything, so every asset in the manager reported zero collected however many gifts had
    /// come through it - and "what did this QR code bring in" is the whole reason the screen
    /// exists. Null where the payment tables could not be reached, which shows as zero rather
    /// than failing the page.
    /// </summary>
    public static TrackingAssetListItemResponse ToListItemResponse(
        this TrackingAsset asset,
        string campaignCode,
        string campaignName,
        string channelName,
        string sourceName,
        string mediumName,
        DateTimeOffset now,
        CampaignIncome? income = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new TrackingAssetListItemResponse(
            asset.Id,
            asset.TenantId,
            asset.Code,
            asset.TrackingReference,
            asset.GeneratedUrl,
            asset.CampaignId,
            campaignCode,
            campaignName,
            asset.AssetType,
            asset.ChannelId,
            channelName,
            asset.Destination,
            asset.SourceId,
            sourceName,
            asset.MediumId,
            mediumName,
            asset.ContentTag,
            asset.Status,
            DescribeStatus(asset.Status),
            asset.ActiveFrom,
            asset.ActiveTo,
            asset.IsLiveAt(now),
            asset.UsageCount,
            income?.ConfirmedAmount ?? 0m,
            income?.DonationCount ?? 0,
            income?.DonorCount ?? 0,
            asset.Places.Count,
            asset.UpdatedAtUtc,
            asset.Version);
    }

    /// <summary>The detail panel.</summary>
    public static TrackingAssetDetailResponse ToDetailResponse(
        this TrackingAsset asset,
        string campaignCode,
        string campaignName,
        string channelName,
        string sourceName,
        string mediumName,
        DateTimeOffset now,
        IReadOnlyList<string> permittedActions,
        CampaignIncome? income = null,
        IReadOnlyDictionary<Guid, string>? cityNames = null,
        IReadOnlyDictionary<Guid, string>? stateNames = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new TrackingAssetDetailResponse(
            asset.Id,
            asset.TenantId,
            asset.BusinessUnitId,
            asset.Code,
            asset.TrackingReference,
            asset.GeneratedUrl,
            asset.CampaignId,
            campaignCode,
            campaignName,
            asset.AssetType,
            asset.ChannelId,
            channelName,
            asset.Destination,
            asset.SourceId,
            sourceName,
            asset.MediumId,
            mediumName,
            asset.ContentTag,
            asset.Status,
            DescribeStatus(asset.Status),
            asset.ActiveFrom,
            asset.ActiveTo,
            asset.IsLiveAt(now),
            asset.UsageCount,
            income?.ConfirmedAmount ?? 0m,
            income?.DonationCount ?? 0,
            income?.DonorCount ?? 0,
            income?.RefundedAmount ?? 0m,
            asset.SubmittedByUserId,
            asset.SubmittedAtUtc,
            asset.ApprovedByUserId,
            asset.ApprovedAtUtc,
            asset.CreatedAtUtc,
            asset.CreatedByUserId,
            asset.UpdatedAtUtc,
            asset.UpdatedByUserId,
            asset.Version,
            [.. asset.Places.Select(place => place.ToResponse(cityNames, stateNames))],
            permittedActions);
    }

    public static TrackingAssetPlaceResponse ToResponse(
        this TrackingAssetPlace place,
        IReadOnlyDictionary<Guid, string>? cityNames = null,
        IReadOnlyDictionary<Guid, string>? stateNames = null)
    {
        ArgumentNullException.ThrowIfNull(place);

        return new TrackingAssetPlaceResponse(
            place.Id,
            place.PlaceName,
            place.CityId,
            Lookup(cityNames, place.CityId),
            place.StateId,
            Lookup(stateNames, place.StateId),
            place.Destination,
            [.. place.CustomFields.Select(field =>
                new TrackingAssetCustomFieldResponse(field.Id, field.FieldName, field.Value))]);
    }

    private static string? Lookup(IReadOnlyDictionary<Guid, string>? names, Guid? id) =>
        names is not null && id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;

    public static TrackingAssetExportRow ToExportRow(
        this TrackingAsset asset,
        string campaignCode,
        string channelName,
        string sourceName,
        string mediumName,
        CampaignIncome? income = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new TrackingAssetExportRow(
            asset.Code,
            asset.TrackingReference,
            campaignCode,
            asset.AssetType.ToString(),
            channelName,
            sourceName,
            mediumName,
            asset.Destination,
            asset.GeneratedUrl,
            asset.Status.ToString(),
            asset.ActiveFrom.ToString("u", CultureInfo.InvariantCulture),
            asset.ActiveTo.ToString("u", CultureInfo.InvariantCulture),
            asset.UsageCount.ToString(CultureInfo.InvariantCulture),
            (income?.ConfirmedAmount ?? 0m).ToString(CultureInfo.InvariantCulture),
            (income?.DonationCount ?? 0).ToString(CultureInfo.InvariantCulture));
    }

    public static string DescribeStatus(TrackingAssetStatus status) => status switch
    {
        TrackingAssetStatus.Draft => "Draft - being prepared",
        TrackingAssetStatus.Submitted => "Submitted - awaiting approval",
        TrackingAssetStatus.Approved => "Approved - not yet live",
        TrackingAssetStatus.Active => "Active",
        TrackingAssetStatus.Inactive => "Inactive - no longer resolving",
        _ => status.ToString()
    };

    /// <summary>
    /// What the caller may do to this asset next.
    ///
    /// The same three-part rule the campaign version uses: the state allows it, the caller holds
    /// the permission, and for Approve the caller is independent of whoever created or submitted
    /// it.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        TrackingAsset asset, Guid callerUserId, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.TrackingAssetsView))
        {
            actions.Add("View");
        }

        if (hasPermission(PermissionCodes.TrackingAssetsExport))
        {
            actions.Add("Export");
        }

        if (asset.Status == TrackingAssetStatus.Draft)
        {
            if (hasPermission(PermissionCodes.TrackingAssetsEdit))
            {
                actions.Add("Edit");
            }

            if (hasPermission(PermissionCodes.TrackingAssetsSubmit))
            {
                actions.Add("Submit");
            }

            // A draft nothing points at can be discarded outright. `UsageCount` is checked as
            // well as the status because the handler checks it, and an action offered here that
            // the handler then refuses is worse than one never offered.
            if (asset.UsageCount == 0 && hasPermission(PermissionCodes.TrackingAssetsDeleteDraft))
            {
                actions.Add("DeleteDraft");
            }
        }

        if (asset.Status == TrackingAssetStatus.Submitted
            && hasPermission(PermissionCodes.TrackingAssetsApprove)
            && asset.CanBeApprovedBy(callerUserId))
        {
            actions.Add("Approve");
        }

        if (asset.Status == TrackingAssetStatus.Approved
            && hasPermission(PermissionCodes.TrackingAssetsActivate))
        {
            actions.Add("Activate");
        }

        // THE DISABLE PAIR. A live asset can be REQUESTED for disable by its maker and
        // deactivated by a checker; an asset already carrying a request can only be decided.
        if (asset.Status == TrackingAssetStatus.Active
            && hasPermission(PermissionCodes.TrackingAssetsRequestDisable))
        {
            actions.Add("RequestDisable");
        }

        if (asset.Status is TrackingAssetStatus.Active or TrackingAssetStatus.DisableRequested
            && hasPermission(PermissionCodes.TrackingAssetsDeactivate))
        {
            actions.Add("Deactivate");
        }

        return actions;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
