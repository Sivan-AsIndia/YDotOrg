using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to the campaign readiness checklist.</summary>
public sealed class CampaignReadinessRepository(CampaignDbContext context) : ICampaignReadinessRepository
{
    public async Task AddAsync(CampaignReadinessCheck check, CancellationToken cancellationToken) =>
        await context.CampaignReadinessChecks.AddAsync(check, cancellationToken);

    /// <summary>
    /// One check with its blockers, tracked for editing.
    ///
    /// The blockers are always included because every write path asks whether one is open -
    /// passing a check, raising a blocker, resolving one - so loading them lazily would turn
    /// one query into two on every single path.
    /// </summary>
    public Task<CampaignReadinessCheck?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.CampaignReadinessChecks
            .Include(check => check.Blockers)
            .FirstOrDefaultAsync(check => check.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CampaignReadinessCheck>> GetForCampaignAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        await context.CampaignReadinessChecks
            .AsNoTracking()
            .Include(check => check.Blockers)
            .Where(check => check.CampaignId == campaignId)
            .OrderBy(check => check.Category)
            .ThenBy(check => check.CheckName)
            .ToListAsync(cancellationToken);

    public async Task AddBlockerAsync(
        CampaignReadinessBlocker blocker, CancellationToken cancellationToken) =>
        await context.CampaignReadinessBlockers.AddAsync(blocker, cancellationToken);

    /// <summary>
    /// One blocker with the check it belongs to, and that check's other blockers.
    ///
    /// The siblings are needed because resolving a blocker has to decide whether the check goes
    /// back to Pending, which depends on whether any OTHER blocker is still open.
    /// </summary>
    public Task<CampaignReadinessBlocker?> GetBlockerAsync(
        Guid blockerId, CancellationToken cancellationToken) =>
        context.CampaignReadinessBlockers
            .Include(blocker => blocker.ReadinessCheck)
                .ThenInclude(check => check.Blockers)
            .FirstOrDefaultAsync(blocker => blocker.Id == blockerId, cancellationToken);

    public Task<bool> CheckNameExistsAsync(
        Guid campaignId, string checkName, Guid? excludeCheckId, CancellationToken cancellationToken) =>
        context.CampaignReadinessChecks
            .Where(check => check.CampaignId == campaignId)
            .Where(check => check.CheckName == checkName)
            .Where(check => excludeCheckId == null || check.Id != excludeCheckId)
            .AnyAsync(cancellationToken);

    public void Remove(CampaignReadinessCheck check) => context.CampaignReadinessChecks.Remove(check);
}
