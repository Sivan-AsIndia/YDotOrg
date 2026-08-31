namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The review state of one grouped document submission.
///
/// A SUBMISSION IS THE UNIT A REVIEWER DECIDES ON, and that is why it has its own status rather
/// than the screen totalling up the states of the files inside it. A registration certificate
/// with two supporting scans is one piece of evidence presented three ways; approving two files
/// and rejecting the third leaves the reviewer's actual decision unrecorded.
///
/// <code>
/// Draft
///   |  files are being attached; not yet visible in the review queue
///   v
/// Submitted
///   |
///   v
/// UnderReview ----> Approved
///   |     |
///   |     +-------> Rejected          decided, and final for this submission
///   |
///   +-------------> ReuploadRequested
///                        |  the Organisation replaces a file, which returns it
///                        v  to Submitted with the previous version kept
///                   Submitted
/// </code>
///
/// <see cref="ReuploadRequested"/> is separate from <see cref="Rejected"/> on purpose. "This
/// certificate is unreadable, send a clearer scan" and "we are refusing this organisation" are
/// different messages, and collapsing them means the first one reads as the second.
/// </summary>
public enum TenantDocumentSubmissionStatus
{
    /// <summary>Being assembled. Files may be added and removed freely.</summary>
    Draft = 0,

    /// <summary>Handed to the reviewers. The Organisation can no longer change it.</summary>
    Submitted = 1,

    /// <summary>A reviewer has picked it up, so the queue shows it is being worked on.</summary>
    UnderReview = 2,

    /// <summary>Accepted.</summary>
    Approved = 3,

    /// <summary>Refused, with a mandatory reason.</summary>
    Rejected = 4,

    /// <summary>Sent back for a better copy. The Organisation may replace files and resubmit.</summary>
    ReuploadRequested = 5
}
