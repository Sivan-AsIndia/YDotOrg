using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Owned entity (table don_donor_contacts). Mutation goes through the Donor aggregate root.
/// Section 3.2 defines Name, Description and Status; the rest is what the Donor 360 identity
/// and contact panel needs to show a usable row.
/// </summary>
public class DonorContact : AuditEntity
{
    // ---- Section 3.2 property contract ---------------------------------------------------

    /// <summary>2 to 160 characters, for example "Primary mobile".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum 2000 characters.</summary>
    public string? Description { get; set; }

    public DonorContactStatus Status { get; set; } = DonorContactStatus.Active;

    // ---- Operational columns ---------------------------------------------------------------

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    public ContactChannel Channel { get; set; } = ContactChannel.Mobile;

    /// <summary>The address or number itself. Masked whenever the caller lacks the sensitive-contact permission.</summary>
    public string Value { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public bool IsVerified { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }
}
