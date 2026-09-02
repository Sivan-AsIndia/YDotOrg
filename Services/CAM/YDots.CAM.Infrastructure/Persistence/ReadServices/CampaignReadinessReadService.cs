using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;
using YDots.CAM.Application.Features.CampaignReadiness.Mappings;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the campaign readiness checklist.
///
/// THE CHECKLIST IS READ AS A WHOLE AND IS NOT PAGED. The question it answers is "can this
/// campaign launch?", and half a checklist cannot answer it - a page-two required check that
/// has not passed would be invisible to a screen showing a green tick.
///
/// THE PEOPLE ARE RESOLVED ONCE FOR THE WHOLE SCREEN. A checklist names three sets of users -
/// the campaign's owners, each check's owner, and each blocker's owner - and they overlap
/// heavily. One directory call for the union of the three is what keeps a page that shows a
/// dozen names from issuing a dozen queries.
/// </summary>
public sealed class CampaignReadinessReadService(
    CampaignDbContext context,
    IPeopleDirectory people,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : ICampaignReadinessReadService
{
    public async Task<CampaignReadinessResponse?> GetForCampaignAsync(
        Guid campaignId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // The campaign is resolved through the Organisation filter FIRST. That is what makes a
        // readiness request for another Organisation's campaign answer "not found" rather than
        // "an empty checklist", which would read as "nothing to do" on the screen.
        var campaign = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
            .Include(entity => entity.Owners)
            .FirstOrDefaultAsync(entity => entity.Id == campaignId, cancellationToken);

        if (campaign is null)
        {
            return null;
        }

        var checks = await context.CampaignReadinessChecks
            .AsNoTracking()
            .Include(check => check.Blockers)
            .Where(check => check.CampaignId == campaignId)
            .OrderBy(check => check.Category)
            .ThenBy(check => check.CheckName)
            .ToListAsync(cancellationToken);

        var resolved = await ResolvePeopleAsync(
            [
                .. campaign.Owners.Select(owner => owner.OwnerId),
                .. checks.Where(check => check.OwnerUserId.HasValue).Select(check => check.OwnerUserId!.Value),
                .. checks.SelectMany(check => check.Blockers).Select(blocker => blocker.OwnerUserId)
            ],
            cancellationToken);

        return ReadinessMappingConfig.ToReadinessResponse(
            campaign,
            checks,
            clock.TodayUtc,
            ReadinessMappingConfig.CampaignActionsFor(
                campaign, currentUser.UserId, currentUser.HasPermission),
            resolved);
    }

    public async Task<ReadinessCheckDetailResponse?> GetCheckAsync(
        Guid checkId, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var check = await context.CampaignReadinessChecks
            .AsNoTracking()
            .Include(entity => entity.Blockers)
            .FirstOrDefaultAsync(entity => entity.Id == checkId, cancellationToken);

        if (check is null)
        {
            return null;
        }

        // The check itself is Organisation-filtered, but a caller scoped to their OWN records
        // must also be on the campaign - otherwise somebody scoped to their own work could read
        // the checklist of a colleague's campaign in the same Organisation.
        if (scope.IsOwnRecordsOnly)
        {
            var isOwn = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
                .AnyAsync(campaign => campaign.Id == check.CampaignId, cancellationToken);

            if (!isOwn)
            {
                return null;
            }
        }

        var resolved = await ResolvePeopleAsync(
            [
                .. check.OwnerUserId.HasValue ? new[] { check.OwnerUserId.Value } : [],
                .. check.Blockers.Select(blocker => blocker.OwnerUserId)
            ],
            cancellationToken);

        return check.ToDetailResponse(
            clock.TodayUtc,
            ReadinessMappingConfig.PermittedActionsFor(check, currentUser.HasPermission),
            resolved);
    }

    /// <summary>
    /// Names for every user id the screen is about to show, in one call.
    ///
    /// The empty ids are dropped before asking: an unassigned check carries Guid.Empty, and
    /// sending it to the directory would be asking identity about a user that cannot exist.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, PersonSummary>> ResolvePeopleAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var wanted = userIds.Where(id => id != Guid.Empty).Distinct().ToArray();

        if (wanted.Length == 0 || !tenantContext.HasTenant)
        {
            return new Dictionary<Guid, PersonSummary>();
        }

        return await people.GetPeopleAsync(
            tenantContext.RequireTenantId(), wanted, cancellationToken);
    }

    private static IQueryable<Campaign> ApplyScope(IQueryable<Campaign> query, AccessScope scope) =>
        scope.IsOwnRecordsOnly
            ? query.Where(campaign =>
                campaign.CreatedByUserId == scope.UserId
                || campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            : query;
}
