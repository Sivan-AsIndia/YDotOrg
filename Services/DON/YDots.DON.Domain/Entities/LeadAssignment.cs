using YDots.DON.Domain.Common;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// One ownership change (table don_lead_assignments). Append only, so "Inspect history" on
/// SCR-DON-006 can show who moved the lead, when and why.
/// </summary>
public class LeadAssignment : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    public Guid LeadId { get; set; }

    public Lead? Lead { get; set; }

    public Guid? PreviousOwnerUserId { get; set; }

    public string? PreviousOwnerName { get; set; }

    public Guid NewOwnerUserId { get; set; }

    public string NewOwnerName { get; set; } = string.Empty;

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string AssignmentReason { get; set; } = string.Empty;

    public DateTimeOffset EffectiveAtUtc { get; set; }

    public Guid AssignedByUserId { get; set; }

    /// <summary>True when the row came from the Bulk route action rather than a single Assign.</summary>
    public bool IsBulkRoute { get; set; }
}
