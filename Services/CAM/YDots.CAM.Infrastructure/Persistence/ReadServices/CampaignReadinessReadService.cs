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
/// </summary>
public sealed class CampaignReadinessReadService(
    CampaignDbContext context,
    ICurrentUser currentUser,
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

        return ReadinessMappingConfig.ToReadinessResponse(campaign, checks, clock.TodayUtc);
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
        // must also be on the campaign - otherwise a Campaign Owner could read the checklist of
        // a colleague's campaign in the same Organisation.
        if (scope.IsOwnRecordsOnly)
        {
            var isOwn = await ApplyScope(context.Campaigns.AsNoTracking(), scope)
                .AnyAsync(campaign => campaign.Id == check.CampaignId, cancellationToken);

            if (!isOwn)
            {
                return null;
            }
        }

        return check.ToDetailResponse(
            clock.TodayUtc,
            ReadinessMappingConfig.PermittedActionsFor(check, currentUser.HasPermission));
    }

    private static IQueryable<Campaign> ApplyScope(IQueryable<Campaign> query, AccessScope scope) =>
        scope.IsOwnRecordsOnly
            ? query.Where(campaign =>
                campaign.CreatedByUserId == scope.UserId
                || campaign.Owners.Any(owner => owner.OwnerId == scope.UserId))
            : query;
}
