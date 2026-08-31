using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A batch of access reviews issued together — the quarterly recertification.
///
/// Individual reviews are grouped so progress can be reported as a whole ("62 of 180 done,
/// 12 overdue"), which is the number a compliance officer actually needs, and so closing
/// the campaign can apply every outstanding decision in one step.
/// </summary>
public class AccessReviewCampaign : TenantEntity, ICodedEntity
{
    /// <summary>Unique inside the Tenant, for example REV-2026-Q3.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public AccessReviewCampaignStatus Status { get; set; } = AccessReviewCampaignStatus.Draft;

    public DateTimeOffset StartsAtUtc { get; set; }

    public DateTimeOffset DueAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public Guid? ClosedByUserId { get; set; }

    /// <summary>Snapshot counts, so the progress bar needs no aggregate query per render.</summary>
    public int TotalReviewCount { get; set; }

    public int CompletedReviewCount { get; set; }

    public int OverdueReviewCount { get; set; }

    /// <summary>
    /// When true, any review still open at the due date is treated as Revoke rather than
    /// Retain. Failing closed is the right default for a recertification: silence should
    /// not renew access.
    /// </summary>
    public bool RevokeOnNoResponse { get; set; }

    public ICollection<AccessReview> Reviews { get; set; } = [];

    public int PercentComplete => TotalReviewCount == 0
        ? 0
        : (int)Math.Round(CompletedReviewCount * 100.0 / TotalReviewCount);
}
