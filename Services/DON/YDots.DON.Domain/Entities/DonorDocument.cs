using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// A document linked to a donor (table don_donor_documents). Only the reference and the
/// metadata live here — never the bytes, which is what section 10 redaction requires.
/// </summary>
public class DonorDocument : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    public Guid DonorId { get; set; }

    public Donor? Donor { get; set; }

    /// <summary>Stable reference in the document store.</summary>
    public string Reference { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DocumentClassification Classification { get; set; } = DocumentClassification.Internal;

    public string? ContentType { get; set; }

    public long? SizeInBytes { get; set; }

    /// <summary>Result of the virus and content scan, shown separately from the upload status.</summary>
    public string? ScanStatus { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
