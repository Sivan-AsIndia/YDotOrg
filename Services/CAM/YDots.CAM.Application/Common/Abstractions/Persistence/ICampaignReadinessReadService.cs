using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>Read-side projections for the campaign readiness checklist.</summary>
public interface ICampaignReadinessReadService
{
    /// <summary>
    /// The whole checklist for one campaign, with its blockers and its launch verdict.
    ///
    /// NOT PAGED, deliberately. A readiness checklist is read as a whole - the question it
    /// answers is "can this campaign launch?", and half a checklist cannot answer it.
    /// </summary>
    Task<CampaignReadinessResponse?> GetForCampaignAsync(
        Guid campaignId, AccessScope scope, CancellationToken cancellationToken);

    Task<ReadinessCheckDetailResponse?> GetCheckAsync(
        Guid checkId, AccessScope scope, CancellationToken cancellationToken);
}
