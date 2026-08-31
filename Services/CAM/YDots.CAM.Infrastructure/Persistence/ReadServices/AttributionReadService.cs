using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.Attribution.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;
using YDots.CAM.Infrastructure.Multitenancy;

namespace YDots.CAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the attribution explorer.
///
/// IT JOINS TWO WORLDS. The donations come from the payments tables through
/// <see cref="IFinancialDirectory"/>; the campaigns and tracking assets are CAM's own. Neither half
/// can answer the screen's question alone - "why is this gift credited to that campaign?" is
/// precisely a question about the join.
///
/// AN UNATTRIBUTED DONATION IS A RESULT, NOT AN ERROR. Many people type the address in rather than
/// following a link, and a screen that hid those gifts would make tracked channels look like the
/// whole picture. They appear, marked as untraced, and the summary counts them in their own row.
/// </summary>
public sealed class AttributionReadService(
    CampaignDbContext context,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IFinancialDirectory financial,
    IAttributionCorrectionRepository corrections) : IAttributionReadService
{
    public async Task<PagedResponse<AttributionListItemResponse>> SearchAsync(
        AttributionSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var size = filter.PageSize is < 1 or > 200 ? 25 : filter.PageSize;

        var campaignId = await NarrowCampaignAsync(filter.CampaignId, scope, cancellationToken);

        // A caller scoped to their own records, with no campaign of their own, sees nothing rather
        // than everything. Falling through to an unfiltered read is how a scope check becomes a
        // no-op.
        if (scope.IsOwnRecordsOnly && campaignId is null && filter.CampaignId is not null)
        {
            return PagedResponse<AttributionListItemResponse>.Empty(page, size);
        }

        var (donations, total) = await financial.SearchAttributedDonationsAsync(
            tenant.TenantId ?? Guid.Empty,
            campaignId,
            filter.TrackingAssetId,
            filter.Search,
            filter.FromUtc,
            filter.ToUtc,
            filter.AttributedOnly,
            page,
            size,
            cancellationToken);

        var items = await ProjectAsync(donations, scope, cancellationToken);

        return new PagedResponse<AttributionListItemResponse>(items, total, page, size);
    }

    public async Task<AttributionDetailResponse?> GetAsync(
        Guid donationId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var donation = await financial.GetAttributedDonationAsync(
            tenant.TenantId ?? Guid.Empty, donationId, cancellationToken);

        if (donation is null)
        {
            return null;
        }

        var campaign = donation.CampaignId is { } id
            ? await context.Campaigns.AsNoTracking()
                .Include(entity => entity.Owners)
                .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken)
            : null;

        // A caller who may only see their own records must be on the campaign. Without this, a
        // campaign owner could read every donation in the organisation by asking for one at a time.
        if (scope.IsOwnRecordsOnly && campaign is not null
            && campaign.CreatedByUserId != scope.UserId
            && !campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
        {
            return null;
        }

        var asset = donation.TrackingAssetId is { } assetId
            ? await context.TrackingAssets.AsNoTracking()
                .FirstOrDefaultAsync(entity => entity.Id == assetId, cancellationToken)
            : null;

        var open = await corrections.GetDonationsWithOpenRequestsAsync([donationId], cancellationToken);
        var hasOpen = open.Contains(donationId);

        return new AttributionDetailResponse
        {
            DonationId = donation.DonationId,
            Reference = donation.Reference,
            ReceivedAtUtc = donation.ReceivedAtUtc,
            Amount = donation.Amount,
            CurrencyCode = donation.CurrencyCode,
            Status = donation.Status,
            DonorName = donation.DonorName,
            DonorId = donation.DonorId,
            CampaignId = donation.CampaignId,
            CampaignCode = campaign?.Code ?? string.Empty,
            CampaignName = donation.CampaignName,
            CampaignStatus = campaign?.Status.ToString() ?? string.Empty,
            TrackingAssetId = donation.TrackingAssetId,
            TrackingReference = donation.TrackingReference,
            AssetType = asset?.AssetType,
            AssetDestination = asset?.Destination,
            ChannelName = donation.ChannelName,
            SourceName = donation.SourceName,
            MediumName = donation.MediumName,
            IsAttributed = donation.IsAttributed,
            AttributionDescription = Describe(donation),
            HasOpenCorrectionRequest = hasOpen,
            Trace = BuildTrace(donation, campaign, asset),
            PermittedActions = PermittedActions(hasOpen)
        };
    }

    /// <summary>
    /// How income breaks down.
    ///
    /// EVERY SHARE IS OF THE TOTAL INCLUDING UNTRACED GIFTS, not of the traced portion. A channel
    /// shown as "60% of income" when it is 60% of the third that could be traced would overstate it
    /// threefold, and that is the number somebody would use to decide where to spend next year.
    /// </summary>
    public async Task<AttributionSummaryResponse> GetSummaryAsync(
        Guid? campaignId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var narrowed = await NarrowCampaignAsync(campaignId, scope, cancellationToken);

        // A LARGE PAGE, not everything. The breakdown is computed over the donations rather than
        // aggregated in SQL because the grouping keys live on CAM's side of the join; the cap keeps
        // that honest about its cost.
        var (donations, _) = await financial.SearchAttributedDonationsAsync(
            tenant.TenantId ?? Guid.Empty,
            narrowed,
            null, null, null, null, null,
            1, 200,
            cancellationToken);

        var total = donations.Sum(donation => donation.Amount);
        var attributed = donations.Where(donation => donation.IsAttributed).ToList();
        var attributedAmount = attributed.Sum(donation => donation.Amount);

        var currency = donations
            .Select(donation => donation.CurrencyCode)
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ?? string.Empty;

        return new AttributionSummaryResponse
        {
            CampaignId = narrowed,
            TotalAmount = total,
            TotalDonations = donations.Count,
            AttributedAmount = attributedAmount,
            AttributedDonations = attributed.Count,
            UnattributedAmount = total - attributedAmount,
            UnattributedDonations = donations.Count - attributed.Count,
            AttributionRate = total == 0m ? 0m : Math.Round(attributedAmount / total * 100m, 2),
            CurrencyCode = currency,
            ByChannel = Breakdown(attributed, donation => donation.ChannelName, total),
            BySource = Breakdown(attributed, donation => donation.SourceName, total),
            ByMedium = Breakdown(attributed, donation => donation.MediumName, total),
            ByAsset = Breakdown(attributed, donation => donation.TrackingReference, total)
        };
    }

    public async Task<IReadOnlyList<AttributionListItemResponse>> ListForExportAsync(
        AttributionSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var campaignId = await NarrowCampaignAsync(filter.CampaignId, scope, cancellationToken);

        var (donations, _) = await financial.SearchAttributedDonationsAsync(
            tenant.TenantId ?? Guid.Empty,
            campaignId,
            filter.TrackingAssetId,
            filter.Search,
            filter.FromUtc,
            filter.ToUtc,
            filter.AttributedOnly,
            1,
            200,
            cancellationToken);

        return await ProjectAsync(donations, scope, cancellationToken);
    }

    // =============================================================================================
    // Internals
    // =============================================================================================

    private async Task<IReadOnlyList<AttributionListItemResponse>> ProjectAsync(
        IReadOnlyList<AttributedDonation> donations,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        if (donations.Count == 0)
        {
            return [];
        }

        var campaignIds = donations
            .Where(donation => donation.CampaignId is not null)
            .Select(donation => donation.CampaignId!.Value)
            .Distinct()
            .ToList();

        var campaigns = await context.Campaigns
            .AsNoTracking()
            .Include(campaign => campaign.Owners)
            .Where(campaign => campaignIds.Contains(campaign.Id))
            .ToDictionaryAsync(campaign => campaign.Id, cancellationToken);

        var assetIds = donations
            .Where(donation => donation.TrackingAssetId is not null)
            .Select(donation => donation.TrackingAssetId!.Value)
            .Distinct()
            .ToList();

        var assets = await context.TrackingAssets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);

        var open = await corrections.GetDonationsWithOpenRequestsAsync(
            donations.Select(donation => donation.DonationId).ToList(), cancellationToken);

        var rows = new List<AttributionListItemResponse>(donations.Count);

        foreach (var donation in donations)
        {
            Campaign? campaign = null;

            if (donation.CampaignId is { } id)
            {
                campaigns.TryGetValue(id, out campaign);
            }

            // The own-records filter, applied per row. A donation on somebody else's campaign is
            // dropped rather than blanked: a row showing an amount with no campaign would still
            // disclose the gift.
            if (scope.IsOwnRecordsOnly && campaign is not null
                && campaign.CreatedByUserId != scope.UserId
                && !campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            {
                continue;
            }

            TrackingAsset? asset = null;

            if (donation.TrackingAssetId is { } assetId)
            {
                assets.TryGetValue(assetId, out asset);
            }

            var hasOpen = open.Contains(donation.DonationId);

            rows.Add(new AttributionListItemResponse
            {
                DonationId = donation.DonationId,
                Reference = donation.Reference,
                ReceivedAtUtc = donation.ReceivedAtUtc,
                Amount = donation.Amount,
                CurrencyCode = donation.CurrencyCode,
                Status = donation.Status,
                CampaignId = donation.CampaignId,
                CampaignCode = campaign?.Code ?? string.Empty,
                CampaignName = donation.CampaignName,
                TrackingAssetId = donation.TrackingAssetId,
                TrackingReference = donation.TrackingReference,
                AssetType = asset?.AssetType,
                ChannelName = donation.ChannelName,
                SourceName = donation.SourceName,
                MediumName = donation.MediumName,
                DonorName = donation.DonorName,
                DonorId = donation.DonorId,
                IsAttributed = donation.IsAttributed,
                AttributionDescription = Describe(donation),
                HasOpenCorrectionRequest = hasOpen,
                PermittedActions = PermittedActions(hasOpen)
            });
        }

        return rows;
    }

    /// <summary>
    /// Narrows a campaign filter to what the caller may see.
    ///
    /// Returns null when the caller is unscoped or asked for nothing in particular; returns the
    /// campaign when they may see it; and returns null having established they may NOT see it only
    /// after the caller has checked - which is why the call site tests both.
    /// </summary>
    private async Task<Guid?> NarrowCampaignAsync(
        Guid? campaignId, AccessScope scope, CancellationToken cancellationToken)
    {
        if (campaignId is not { } id)
        {
            return null;
        }

        if (!scope.IsOwnRecordsOnly)
        {
            return id;
        }

        var permitted = await context.Campaigns
            .AsNoTracking()
            .AnyAsync(
                campaign => campaign.Id == id
                    && (campaign.CreatedByUserId == scope.UserId
                        || campaign.Owners.Any(owner => owner.OwnerId == scope.UserId)),
                cancellationToken);

        return permitted ? id : null;
    }

    private static IReadOnlyList<AttributionBreakdownRow> Breakdown(
        IReadOnlyList<AttributedDonation> donations,
        Func<AttributedDonation, string> key,
        decimal total) =>
        donations
            .GroupBy(donation => string.IsNullOrWhiteSpace(key(donation)) ? "Unspecified" : key(donation))
            .Select(group =>
            {
                var amount = group.Sum(donation => donation.Amount);

                return new AttributionBreakdownRow(
                    group.Key,
                    group.Key,
                    amount,
                    group.Count(),
                    total == 0m ? 0m : Math.Round(amount / total * 100m, 2));
            })
            .OrderByDescending(row => row.Amount)
            .ToList();

    /// <summary>
    /// The attribution in one sentence.
    ///
    /// WRITTEN OUT RATHER THAN LEFT TO THE SCREEN, because the distinction between "traced to a QR
    /// code" and "recorded against a campaign by hand" is the thing people misread, and a sentence
    /// is harder to misread than a tick.
    /// </summary>
    private static string Describe(AttributedDonation donation)
    {
        if (donation.IsAttributed)
        {
            var parts = new[] { donation.ChannelName, donation.SourceName, donation.MediumName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            var route = parts.Count > 0 ? string.Join(" · ", parts) : "an unnamed route";

            return $"Traced to {donation.TrackingReference} via {route}.";
        }

        return donation.CampaignId is not null
            ? "Recorded against the campaign directly. No tracking asset was involved, so this gift "
              + "is not evidence that any channel produced it."
            : "Not attributed to any campaign.";
    }

    /// <summary>The hops, in the order they happened.</summary>
    private static IReadOnlyList<AttributionTraceStepResponse> BuildTrace(
        AttributedDonation donation, Campaign? campaign, TrackingAsset? asset)
    {
        var steps = new List<AttributionTraceStepResponse>
        {
            new(
                "donation",
                "The donation",
                $"Received {donation.ReceivedAtUtc:dd MMM yyyy HH:mm} UTC",
                [
                    new("reference", "Donation reference", donation.Reference, true),
                    new("amount", "Amount", $"{donation.Amount:N2} {donation.CurrencyCode}", false),
                    new("status", "Status", donation.Status, false),
                    new("donor", "Donor", donation.DonorName, false)
                ])
        };

        if (asset is not null)
        {
            steps.Add(new AttributionTraceStepResponse(
                "asset",
                "The tracking asset the donor followed",
                $"{asset.AssetType} created for this campaign",
                [
                    new("reference", "Tracking reference", donation.TrackingReference, true),
                    new("type", "Asset type", asset.AssetType.ToString(), false),
                    new("destination", "Destination", asset.Destination, true),
                    new("status", "Asset status", asset.Status.ToString(), false)
                ]));

            steps.Add(new AttributionTraceStepResponse(
                "route",
                "How it was reached",
                "Channel, source and medium recorded on the asset",
                [
                    new("channel", "Channel", donation.ChannelName, false),
                    new("source", "Source", donation.SourceName, false),
                    new("medium", "Medium", donation.MediumName, false)
                ]));
        }
        else
        {
            // AN EXPLICIT STEP RATHER THAN A GAP. A trail that simply stopped would read as though
            // the data were missing; this says the gift arrived without tracking, which is a fact
            // about the donation rather than a fault in the record.
            steps.Add(new AttributionTraceStepResponse(
                "untracked",
                "No tracking asset",
                "The donor did not arrive through a tracked link or code",
                [
                    new(
                        "explanation",
                        "What this means",
                        "The gift is real and complete. It simply cannot be credited to any "
                        + "particular channel, because nothing recorded how the donor got here.",
                        false)
                ]));
        }

        if (campaign is not null)
        {
            steps.Add(new AttributionTraceStepResponse(
                "campaign",
                "The campaign it was credited to",
                campaign.Status.ToString(),
                [
                    new("code", "Campaign code", campaign.Code, true),
                    new("name", "Campaign", campaign.Name, false),
                    new("status", "Status", campaign.Status.ToString(), false),
                    new(
                        "window",
                        "Running",
                        $"{campaign.StartDate:dd MMM yyyy} to {campaign.EndDate:dd MMM yyyy}",
                        false)
                ]));
        }

        return steps;
    }

    private IReadOnlyList<string> PermittedActions(bool hasOpenCorrection)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.AttributionView))
        {
            actions.Add("View");
        }

        // NOT OFFERED WHILE ONE IS ALREADY OPEN. Two requests against one donation means two people
        // investigating the same gift without knowing about each other, which is exactly what the
        // open flag exists to prevent.
        if (!hasOpenCorrection && currentUser.HasPermission(PermissionCodes.AttributionRequestCorrection))
        {
            actions.Add("RequestCorrection");
        }

        if (currentUser.HasPermission(PermissionCodes.AttributionExport))
        {
            actions.Add("Export");
        }

        return actions;
    }
}
