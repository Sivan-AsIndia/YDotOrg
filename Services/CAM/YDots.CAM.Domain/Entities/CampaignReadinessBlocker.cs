using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// Something standing in the way of a readiness check passing, assigned to somebody to clear.
///
/// It is RESOLVED rather than deleted, so the record of what held a campaign up survives the
/// campaign going live - which is the only reason anybody looks at a blocker after the fact.
/// </summary>
public sealed class CampaignReadinessBlocker : TenantEntity
{
    public Guid CampaignReadinessCheckId { get; set; }

    /// <summary>Who is expected to clear it.</summary>
    public Guid OwnerUserId { get; set; }

    public string BlockerNote { get; set; } = string.Empty;

    public bool IsResolved { get; set; }

    /// <summary>Who cleared it and when. Null while it is still open.</summary>
    public Guid? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public string? ResolutionNote { get; set; }

    public CampaignReadinessCheck ReadinessCheck { get; set; } = default!;
}
