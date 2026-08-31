using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One item on a campaign pre-launch readiness checklist.
///
/// <see cref="RequiredForLaunch"/> is what gives the checklist teeth: an optional check that
/// fails is information, while a required one that has not passed blocks the launch. The
/// distinction lives on the row rather than in the launch handler, so the checklist screen can
/// show an operator exactly what is standing between them and going live.
/// </summary>
public sealed class CampaignReadinessCheck : TenantEntity
{
    public Guid CampaignId { get; set; }

    public string CheckName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ReadinessCheckCategory Category { get; set; }

    public string SuccessCriteria { get; set; } = string.Empty;

    /// <summary>A failed or pending check with this set blocks the campaign from launching.</summary>
    public bool RequiredForLaunch { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Notes { get; set; }

    public ReadinessCheckStatus Status { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public ICollection<CampaignReadinessBlocker> Blockers { get; set; } = [];

    /// <summary>True when this check is one of the things standing between the campaign and launch.</summary>
    public bool BlocksLaunch => RequiredForLaunch && Status != ReadinessCheckStatus.Passed;

    /// <summary>True while any blocker raised against it is still open.</summary>
    public bool HasOpenBlockers => Blockers.Any(blocker => !blocker.IsResolved);
}
