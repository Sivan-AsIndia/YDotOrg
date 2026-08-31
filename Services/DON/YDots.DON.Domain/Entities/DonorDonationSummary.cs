using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Projection of the donation totals a donor has reached, by stage (table
/// don_donor_donation_summaries).
///
/// The amounts belong to the donations section, not to this one. Keeping a local projection is
/// what lets Donor 360 render without a synchronous call to another service, and the two
/// timestamps are why the panel can be honest about it: AsAtUtc is the cut-off the numbers
/// describe, RefreshedAtUtc is when this row last heard from the source.
/// </summary>
public class DonorDonationSummary : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    public DonationStage Stage { get; set; }

    /// <summary>ISO 4217 code, for example INR.</summary>
    public string Currency { get; set; } = "INR";

    public decimal TotalAmount { get; set; }

    public int TransactionCount { get; set; }

    /// <summary>The cut-off the totals describe.</summary>
    public DateTimeOffset AsAtUtc { get; set; }

    /// <summary>When the projection last heard from the owning section.</summary>
    public DateTimeOffset RefreshedAtUtc { get; set; }
}
