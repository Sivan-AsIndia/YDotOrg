using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Owned entity (table don_donor_interactions). One conversation, note or visit.
/// Feeds the Conversations and Activity history panels on Donor 360.
/// </summary>
public class DonorInteraction : AuditEntity, IOrganisationOwned
{
    // ---- Section 3.4 property contract ---------------------------------------------------

    /// <summary>2 to 160 characters, for example "Introduction call".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum 2000 characters.</summary>
    public string? Description { get; set; }

    public DonorInteractionStatus Status { get; set; } = DonorInteractionStatus.Active;

    // ---- Operational columns ---------------------------------------------------------------

    public Guid? DonorId { get; set; }

    public Donor? Donor { get; set; }

    public Guid? LeadId { get; set; }

    public Guid OrganisationId { get; set; }

    public InteractionType InteractionType { get; set; } = InteractionType.Note;

    public ConsentChannel? Channel { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public ContactOutcome Outcome { get; set; } = ContactOutcome.NotContacted;

    public Guid PerformedByUserId { get; set; }

    public string? PerformedByName { get; set; }
}
