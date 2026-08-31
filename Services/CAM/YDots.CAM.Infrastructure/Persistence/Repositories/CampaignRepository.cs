using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to the campaign aggregate.
///
/// EVERY QUERY HERE GOES THROUGH THE ORGANISATION QUERY FILTER, with no
/// <c>IgnoreQueryFilters</c> anywhere in the file. That is what makes the whole repository safe
/// by construction: there is no method here that can be talked into returning another
/// Organisation's campaign, which is also why none of them takes a TenantId.
/// </summary>
public sealed class CampaignRepository(CampaignDbContext context) : ICampaignRepository
{
    public async Task AddAsync(Campaign campaign, CancellationToken cancellationToken) =>
        await context.Campaigns.AddAsync(campaign, cancellationToken);

    /// <summary>
    /// One campaign with its owners and channels, TRACKED so a handler can change it.
    ///
    /// The two collections are included because every write path touches them: an update
    /// replaces both sets, and the permitted-action list counts owners. Loading them lazily
    /// would turn one query into three on the path that always needs all three.
    /// </summary>
    public Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Campaigns
            .Include(campaign => campaign.Owners)
            .Include(campaign => campaign.Channels)
            .FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code, Guid? excludeCampaignId, CancellationToken cancellationToken) =>
        context.Campaigns
            .Where(campaign => campaign.Code == code)
            .Where(campaign => excludeCampaignId == null || campaign.Id != excludeCampaignId)
            .AnyAsync(cancellationToken);

    /// <summary>
    /// The outstanding close request, if any.
    ///
    /// A filtered unique index guarantees there is at most one, so FirstOrDefault is honest
    /// here rather than hiding a set the caller should have reasoned about.
    /// </summary>
    public Task<CampaignLifecycleAction?> GetPendingCloseRequestAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        context.CampaignLifecycleActions
            .Where(action => action.CampaignId == campaignId)
            .Where(action => action.ActionType == CampaignLifecycleActionType.RequestClose)
            .Where(action => action.ActionStatus == CampaignLifecycleActionStatus.Pending)
            .OrderByDescending(action => action.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddLifecycleActionAsync(
        CampaignLifecycleAction action, CancellationToken cancellationToken) =>
        await context.CampaignLifecycleActions.AddAsync(action, cancellationToken);

    /// <summary>
    /// Required readiness checks that have not passed.
    ///
    /// A check with an OPEN BLOCKER counts as outstanding even if its status somehow says
    /// Passed. The two should never disagree - raising a blocker fails the check - but the
    /// launch gate is the wrong place to assume that, because the cost of being wrong is a
    /// campaign going live without its payment configuration.
    /// </summary>
    public async Task<IReadOnlyList<CampaignReadinessCheck>> GetOutstandingRequiredChecksAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        await context.CampaignReadinessChecks
            .AsNoTracking()
            .Where(check => check.CampaignId == campaignId)
            .Where(check => check.RequiredForLaunch)
            .Where(check => check.Status != ReadinessCheckStatus.Passed
                            || check.Blockers.Any(blocker => !blocker.IsResolved))
            .OrderBy(check => check.Category)
            .ThenBy(check => check.CheckName)
            .ToListAsync(cancellationToken);

    public Task<int> CountTrackingAssetsAsync(Guid campaignId, CancellationToken cancellationToken) =>
        context.TrackingAssets.CountAsync(asset => asset.CampaignId == campaignId, cancellationToken);

    public void Delete(Campaign campaign) => context.Campaigns.Remove(campaign);
}
