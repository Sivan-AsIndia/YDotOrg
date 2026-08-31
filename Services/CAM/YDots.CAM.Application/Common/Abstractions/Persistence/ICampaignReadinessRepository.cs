using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the campaign readiness checklist.
///
/// The projection that used to live here - <c>GetByCampaignIdAsync</c> returning a fully
/// shaped <c>CampaignReadinessResponse</c> - moved to
/// <see cref="ICampaignReadinessReadService"/>. A repository returning a response DTO made the
/// persistence contract depend on the features folder, with the dependency pointing the wrong
/// way through the layers.
/// </summary>
public interface ICampaignReadinessRepository
{
    Task AddAsync(CampaignReadinessCheck check, CancellationToken cancellationToken);

    /// <summary>One check with its blockers loaded, for editing.</summary>
    Task<CampaignReadinessCheck?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every check on one campaign, with blockers. The checklist is read as a whole.</summary>
    Task<IReadOnlyList<CampaignReadinessCheck>> GetForCampaignAsync(
        Guid campaignId, CancellationToken cancellationToken);

    Task AddBlockerAsync(CampaignReadinessBlocker blocker, CancellationToken cancellationToken);

    /// <summary>One blocker, loaded with the check it belongs to.</summary>
    Task<CampaignReadinessBlocker?> GetBlockerAsync(Guid blockerId, CancellationToken cancellationToken);

    /// <summary>Whether a check name is already used on the same campaign.</summary>
    Task<bool> CheckNameExistsAsync(
        Guid campaignId, string checkName, Guid? excludeCheckId, CancellationToken cancellationToken);

    void Remove(CampaignReadinessCheck check);
}
