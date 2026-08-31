using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One lifecycle transition requested against a campaign: activate, pause, resume, request
/// close, approve close, cancel a draft.
///
/// IT IS A RECORD OF A REQUEST, NOT JUST A LOG LINE. Several of these transitions need an
/// approval that arrives later and from somebody else, so the row carries its own status and
/// its own approver - which is what lets a close request sit Pending while the campaign keeps
/// running, and what the audit trail reads afterwards to show who decided what.
/// </summary>
public class CampaignLifecycleAction : AuditEntity
{
    public Guid CampaignId { get; set; }

    public CampaignLifecycleActionType ActionType { get; set; }

    /// <summary>When the transition takes effect, which may be later than when it was requested.</summary>
    public DateTimeOffset EffectiveAtUtc { get; set; }

    public string? ReasonCategory { get; set; }

    public string? DetailedReason { get; set; }

    /// <summary>What this means for donors already in flight. Shown on the confirmation screen.</summary>
    public string? CommunicationImpact { get; set; }

    public string? ClosureSummary { get; set; }

    public CampaignLifecycleActionStatus ActionStatus { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Campaign Campaign { get; set; } = default!;

    /// <summary>The same independence rule campaigns and tracking assets use.</summary>
    public bool CanBeApprovedBy(Guid userId) =>
        RequestedByUserId != userId && CreatedByUserId != userId;
}
