using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Owned entity (table don_donor_tags). A short label attached to a donor, for example
/// "Major giver" or "Volunteer". Mutation goes through the Donor aggregate root.
/// </summary>
public class DonorTag : AuditEntity
{
    // ---- Section 3.6 property contract ---------------------------------------------------

    /// <summary>2 to 160 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum 2000 characters.</summary>
    public string? Description { get; set; }

    public DonorTagStatus Status { get; set; } = DonorTagStatus.Active;

    // ---- Operational columns ---------------------------------------------------------------

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    /// <summary>Stable upper-case code so a filter never depends on the display label.</summary>
    public string Code { get; set; } = string.Empty;
}
