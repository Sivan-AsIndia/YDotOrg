using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One person accountable for a campaign.
///
/// AN <see cref="AuditEntity"/> RATHER THAN A BARE JOIN ROW, because the module brief requires
/// ownership history to be auditable: the inherited CreatedBy and CreatedAt answer "who
/// assigned this owner, and when" without a separate history table.
///
/// It carries no TenantId of its own. It is reachable only through its Campaign, which is
/// Tenant-owned and filtered - so a row here cannot be seen except through a campaign the
/// caller was already entitled to read.
/// </summary>
public class CampaignOwner : AuditEntity
{
    public Guid CampaignId { get; set; }

    /// <summary>The IAM user who owns the campaign. Not an FK: IAM is a separate service.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Marks the one owner of record where several are listed. Used when a notification has to
    /// reach a single person rather than everybody.
    /// </summary>
    public bool IsPrimary { get; set; }

    public Campaign Campaign { get; set; } = default!;
}
