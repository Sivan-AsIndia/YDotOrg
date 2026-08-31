using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// A pledge the donor made (table don_donor_promises). Read-only on Donor 360: the fundraiser
/// records it during a conversation, and the donations section is what eventually settles it.
/// </summary>
public class DonorPromise : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    /// <summary>Stable reference, for example PRM-2026-000044.</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code, for example INR.</summary>
    public string Currency { get; set; } = "INR";

    public DateTimeOffset PromisedAtUtc { get; set; }

    public DateTimeOffset? DueAtUtc { get; set; }

    public PromiseStatus Status { get; set; } = PromiseStatus.Open;

    public Guid? CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public string? Notes { get; set; }
}
