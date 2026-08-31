using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// The campaign a lead was acquired through (table don_campaigns). Reference data behind the
/// Campaign selector on lead capture, the lead work queue and the assignment board.
/// </summary>
public class Campaign : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    /// <summary>Stable reference, for example CMP-2026-DIWALI.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Active;

    public DateTimeOffset? StartsAtUtc { get; set; }

    public DateTimeOffset? EndsAtUtc { get; set; }

    public ICollection<Lead> Leads { get; set; } = [];
}
