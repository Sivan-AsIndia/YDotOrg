using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.DTOs;

/// <summary>
/// What the client must know before it lets somebody choose a file.
///
/// SERVED RATHER THAN HARD-CODED, so the sentence the upload box shows and the rule the server
/// enforces are the same number. A limit written into the Angular bundle drifts from the one in
/// configuration the first time either is edited, and the person only discovers it after their
/// upload is refused.
/// </summary>
public sealed record DocumentUploadPolicyResponse(
    int MaximumFileSizeMegabytes,
    long MaximumFileSizeBytes,
    int MaximumFilesPerSubmission,
    IReadOnlyList<string> AllowedContentTypes,
    /// <summary>Extensions matching the accepted types, for the file picker's filter.</summary>
    IReadOnlyList<string> AllowedExtensions,
    int DownloadLinkExpirySeconds);

/// <summary>Opens a new submission. Files are attached afterwards, one call each.</summary>
public sealed record CreateDocumentSubmissionRequest(
    TenantDocumentType DocumentType,
    string? Title = null,
    string? Notes = null,
    string? ReferenceNumber = null,
    DateTimeOffset? IssuedOn = null,
    DateTimeOffset? ExpiresOn = null);

/// <summary>Hands a submission to the reviewers.</summary>
public sealed record SubmitDocumentSubmissionRequest(long ExpectedVersion, string? Notes = null);

/// <summary>
/// The reviewer's decision.
///
/// Three outcomes rather than two. "Send me a clearer scan" is not a refusal, and an interface
/// that offers only approve and reject forces a reviewer to reject an Organisation they have no
/// intention of turning down.
/// </summary>
public enum DocumentSubmissionDecision
{
    Approve = 0,
    Reject = 1,
    RequestReupload = 2
}

/// <summary><see cref="Notes"/> is mandatory for anything but an approval.</summary>
public sealed record DecideDocumentSubmissionRequest(
    DocumentSubmissionDecision Decision,
    long ExpectedVersion,
    string? Notes = null);

/// <summary>One file inside a submission.</summary>
public sealed record SubmissionFileResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? ContentHash,
    TenantDocumentStatus Status,
    DateTimeOffset UploadedAtUtc,
    string? UploadedByName,
    /// <summary>True when the browser can render it in place rather than only download it.</summary>
    bool IsPreviewable,
    /// <summary>Set when this file was replaced by a re-upload; the old bytes are kept.</summary>
    Guid? SupersededByDocumentId);

/// <summary>One grouped submission, as both portals show it.</summary>
public sealed record DocumentSubmissionResponse(
    Guid Id,
    Guid TenantId,
    string? OrganisationName,
    string? OrganisationCode,
    TenantDocumentType DocumentType,
    string DocumentTypeDisplay,
    string? Title,
    string? Notes,
    TenantDocumentSubmissionStatus Status,
    string StatusDisplay,
    DateTimeOffset? SubmittedAtUtc,
    string? SubmittedByName,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? ReviewedByName,
    string? DecisionNotes,
    int ReuploadCount,
    int FileCount,
    long TotalSizeBytes,
    /// <summary>Distinct file kinds inside, so the queue can say "PDF, PNG" without opening it.</summary>
    IReadOnlyList<string> FileKinds,
    IReadOnlyList<SubmissionFileResponse> Files,
    /// <summary>What the CALLER may do from here, computed server-side. The UI draws buttons from this.</summary>
    IReadOnlyList<string> PermittedActions,
    long Version);

/// <summary>A short-lived link to one file, plus how it should be opened.</summary>
public sealed record DocumentDownloadLinkResponse(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Url,
    DateTimeOffset ExpiresAtUtc,
    bool IsPreviewable);
