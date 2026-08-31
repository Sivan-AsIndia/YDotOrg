using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One piece of evidence an Organisation submits, and the files that make it up.
///
/// WHY THIS EXISTS AT ALL. A registration certificate is often not one file: the certificate
/// itself, an amendment page, and a scan of the seal. Before this, each of those was an
/// independent row with its own review state, so a reviewer had three unrelated things to
/// decide on and no way to record the one decision they had actually made. Grouping gives the
/// review a subject.
///
/// THE SUBMISSION IS THE REVIEWABLE THING; the files are its contents. Status, reviewer,
/// timestamps and the decision note live here. A <see cref="TenantDocument"/> underneath keeps
/// only what is true about that file — name, size, checksum, where its bytes are.
///
/// TENANT-OWNED, so the query filter keeps one Organisation's paperwork away from another's.
/// SuperAdmin reads these through the platform path, which resolves the Organisation from the
/// route rather than from the caller.
/// </summary>
public class TenantDocumentSubmission : TenantEntity
{
    public Tenant? Tenant { get; set; }

    /// <summary>What this submission is evidence OF. The files inside may vary in kind.</summary>
    public TenantDocumentType DocumentType { get; set; }

    /// <summary>
    /// What the uploader called it — "Certificate of incorporation, 2019 amendment".
    ///
    /// Optional. Falls back to the document type, which is usually enough.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>Anything the uploader wants the reviewer to know before opening the files.</summary>
    public string? Notes { get; set; }

    public TenantDocumentSubmissionStatus Status { get; set; } = TenantDocumentSubmissionStatus.Draft;

    // ---- Who submitted it, and when -----------------------------------------------------------

    public Guid SubmittedByUserId { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    // ---- The decision -------------------------------------------------------------------------

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewStartedAtUtc { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    /// <summary>
    /// Why it was refused or sent back. Required for both, because a decision the Organisation
    /// cannot act on is a dead end rather than an answer.
    /// </summary>
    public string? DecisionNotes { get; set; }

    /// <summary>How many times this submission has been sent back and re-uploaded.</summary>
    public int ReuploadCount { get; set; }

    public ICollection<TenantDocument> Documents { get; set; } = [];

    // ---- Derived state --------------------------------------------------------------------------

    /// <summary>Waiting on a reviewer. What the pending queue counts.</summary>
    public bool IsAwaitingReview => Status is TenantDocumentSubmissionStatus.Submitted
        or TenantDocumentSubmissionStatus.UnderReview;

    /// <summary>True while the Organisation may still add, replace or remove files.</summary>
    public bool IsEditable => Status is TenantDocumentSubmissionStatus.Draft
        or TenantDocumentSubmissionStatus.ReuploadRequested;

    /// <summary>Decided either way. Approved and Rejected are both final for this submission.</summary>
    public bool IsDecided => Status is TenantDocumentSubmissionStatus.Approved
        or TenantDocumentSubmissionStatus.Rejected;

    /// <summary>
    /// The moves a reviewer or an uploader may legally make from here.
    ///
    /// Kept in one place for the same reason <c>Tenant.AllowedTransitionsFrom</c> is: a handler
    /// that invents its own shortcut is how a submission ends up approved without ever having
    /// been submitted.
    /// </summary>
    public static IReadOnlyList<TenantDocumentSubmissionStatus> AllowedTransitionsFrom(
        TenantDocumentSubmissionStatus status) => status switch
    {
        TenantDocumentSubmissionStatus.Draft => [TenantDocumentSubmissionStatus.Submitted],

        TenantDocumentSubmissionStatus.Submitted =>
        [
            TenantDocumentSubmissionStatus.UnderReview,
            TenantDocumentSubmissionStatus.Approved,
            TenantDocumentSubmissionStatus.Rejected,
            TenantDocumentSubmissionStatus.ReuploadRequested
        ],

        TenantDocumentSubmissionStatus.UnderReview =>
        [
            TenantDocumentSubmissionStatus.Approved,
            TenantDocumentSubmissionStatus.Rejected,
            TenantDocumentSubmissionStatus.ReuploadRequested
        ],

        // Sent back: the Organisation replaces what was wrong and it returns to the queue.
        TenantDocumentSubmissionStatus.ReuploadRequested => [TenantDocumentSubmissionStatus.Submitted],

        TenantDocumentSubmissionStatus.Approved => [],
        TenantDocumentSubmissionStatus.Rejected => [],
        _ => []
    };

    public bool CanTransitionTo(TenantDocumentSubmissionStatus target) =>
        AllowedTransitionsFrom(Status).Contains(target);
}
