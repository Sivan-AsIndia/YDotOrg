using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One document the TenantAdmin uploads while completing the Organisation profile —
/// registration certificate, tax exemption, address proof and so on — and which SuperAdmin
/// reads when deciding whether to approve.
///
/// Tenant-owned, so an Organisation can only ever see its own paperwork. SuperAdmin reads
/// these through the ordinary Tenant-aware path after selecting the Organisation, which is
/// exactly the pattern section 48 of the brief asks for: one set of APIs, with the Tenant
/// context made explicit rather than a parallel SuperAdmin copy of everything.
///
/// The file itself is not stored in the database. <see cref="StoragePath"/> points at the
/// object store; the row carries the metadata, the review state and the checksum.
/// </summary>
public class TenantDocument : TenantEntity
{
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// The submission this file belongs to.
    ///
    /// NULLABLE ONLY FOR THE ROWS THAT PREDATE GROUPING. Everything uploaded since arrives
    /// inside a submission; the older rows are kept rather than back-filled into invented
    /// groups, because a group that nobody actually submitted is worse than an absent one.
    /// New code should treat null as "legacy" and read the review state from the document.
    /// </summary>
    public Guid? SubmissionId { get; set; }

    public TenantDocumentSubmission? Submission { get; set; }

    public TenantDocumentType DocumentType { get; set; }

    /// <summary>The name the person uploading would recognise.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Where the bytes actually live. Never a URL the browser can reach directly.</summary>
    public string StoragePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>
    /// SHA-256 of the content, so a swapped file after approval is detectable.
    ///
    /// COMPUTED BY THE SERVER while the bytes stream into the object store, never taken from
    /// the client. A hash the uploader supplies describes what they meant to send; this one
    /// describes what is actually stored, which is the only version worth checking against.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// The object store's version of these bytes.
    ///
    /// The bucket has versioning on, so replacing a file keeps the old object rather than
    /// destroying it. Holding the version id here is what lets the audit trail re-open exactly
    /// the file a reviewer saw, rather than whatever currently sits at that key.
    /// </summary>
    public string? StorageVersionId { get; set; }

    public TenantDocumentStatus Status { get; set; } = TenantDocumentStatus.Uploaded;

    public DateTimeOffset UploadedAtUtc { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    /// <summary>Required when the document is rejected, so it can be corrected rather than guessed at.</summary>
    public string? ReviewNotes { get; set; }

    /// <summary>Document number as printed on the certificate, when there is one.</summary>
    public string? ReferenceNumber { get; set; }

    public DateTimeOffset? IssuedOn { get; set; }

    /// <summary>Certificates expire. Null means it does not.</summary>
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>
    /// Set when a newer upload replaces this one. The old row is kept rather than deleted so
    /// the approval trail still shows what was actually reviewed at the time.
    /// </summary>
    public Guid? SupersededByDocumentId { get; set; }

    public bool IsExpired(DateTimeOffset asOf) => ExpiresOn.HasValue && ExpiresOn.Value < asOf;
}
