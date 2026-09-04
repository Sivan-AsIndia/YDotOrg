using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.Mappings;

/// <summary>
/// Turns a submission into what the two portals render.
///
/// <c>PermittedActions</c> IS THE IMPORTANT PART. The client draws its buttons from this list
/// rather than working out for itself what is allowed, for the same reason the Organisation
/// screens already do: a rule duplicated in TypeScript drifts from the one in the handler, and
/// the visible result is a button that produces a 409 when pressed. Here the list is computed
/// from the state machine that will actually be consulted.
/// </summary>
public static class DocumentSubmissionMappingConfig
{
    public static DocumentSubmissionResponse ToResponse(
        this TenantDocumentSubmission submission,
        Tenant? tenant,
        IReadOnlyDictionary<Guid, string> names,
        bool callerIsReviewer)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(names);

        var files = submission.Documents
            .OrderBy(document => document.UploadedAtUtc)
            .Select(document => document.ToFileResponse(names))
            .ToList();

        return new DocumentSubmissionResponse(
            submission.Id,
            submission.TenantId,
            tenant?.Name,
            tenant?.Code,
            submission.DocumentType,
            Humanise(submission.DocumentType),
            submission.Title,
            submission.Notes,
            submission.Status,
            Humanise(submission.Status),
            submission.SubmittedAtUtc,
            Name(names, submission.SubmittedByUserId),
            submission.ReviewStartedAtUtc,
            submission.DecidedAtUtc,
            submission.ReviewedByUserId.HasValue ? Name(names, submission.ReviewedByUserId.Value) : null,
            submission.DecisionNotes,
            submission.ReuploadCount,
            files.Count,
            files.Sum(file => file.FileSizeBytes),
            [.. files.Select(file => DocumentContentTypes.ShortLabel(file.ContentType)).Distinct(StringComparer.Ordinal)],
            files,
            // THE ORGANISATION'S OWN STATUS IS PART OF THE ANSWER. Once its registration has been
            // decided the document set is settled, so AddFile / RemoveFile / Submit go - and the
            // client's upload box and buttons, which are drawn from this list and nothing else,
            // go with them. `tenant` is null only where the caller was not given one to map
            // against, and an unknown Organisation is treated as still open so an existing draft
            // is never silently frozen by a missing join.
            PermittedActionsFor(
                submission, callerIsReviewer, tenant?.AcceptsDocumentSubmissions ?? true),
            submission.Version);
    }

    public static SubmissionFileResponse ToFileResponse(
        this TenantDocument document, IReadOnlyDictionary<Guid, string> names)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SubmissionFileResponse(
            document.Id,
            document.FileName,
            document.ContentType,
            document.FileSizeBytes,
            document.ContentHash,
            document.Status,
            document.UploadedAtUtc,
            names.TryGetValue(document.UploadedByUserId, out var name) ? name : null,
            DocumentContentTypes.IsPreviewable(document.ContentType),
            document.SupersededByDocumentId);
    }

    /// <summary>
    /// What this caller may do to this submission, right now.
    ///
    /// The two audiences get different lists from the same state, which is why the flag is
    /// passed in rather than inferred: an Organisation may add files and submit, a reviewer may
    /// pick up and decide, and neither should be offered the other's buttons.
    /// </summary>
    private static IReadOnlyList<string> PermittedActionsFor(
        TenantDocumentSubmission submission, bool callerIsReviewer, bool tenantAcceptsSubmissions)
    {
        var actions = new List<string> { "View" };

        if (callerIsReviewer)
        {
            if (submission.Status == TenantDocumentSubmissionStatus.Submitted)
            {
                actions.Add("StartReview");
            }

            if (submission.IsAwaitingReview)
            {
                actions.Add("Approve");
                actions.Add("Reject");
                actions.Add("RequestReupload");
            }

            return actions;
        }

        if (submission.IsEditable && tenantAcceptsSubmissions)
        {
            actions.Add("AddFile");
            actions.Add("RemoveFile");

            if (submission.Documents.Count > 0)
            {
                // Named for what the Organisation is doing, not for the status it produces:
                // after a send-back this is a resubmission, and calling it "Submit" reads as
                // though the earlier one never happened.
                actions.Add(submission.Status == TenantDocumentSubmissionStatus.ReuploadRequested
                    ? "Resubmit"
                    : "Submit");
            }
        }

        return actions;
    }

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid userId) =>
        names.TryGetValue(userId, out var name) ? name : "—";

    /// <summary>
    /// "ReuploadRequested" becomes "Reupload requested".
    ///
    /// Done here rather than in the client so every screen says the same thing, and so a new
    /// status is readable the moment it exists rather than after somebody remembers to add it
    /// to a lookup in TypeScript.
    /// </summary>
    private static string Humanise(TenantDocumentSubmissionStatus status) => status switch
    {
        TenantDocumentSubmissionStatus.Draft => "Draft",
        TenantDocumentSubmissionStatus.Submitted => "Submitted",
        TenantDocumentSubmissionStatus.UnderReview => "Under review",
        TenantDocumentSubmissionStatus.Approved => "Approved",
        TenantDocumentSubmissionStatus.Rejected => "Rejected",
        TenantDocumentSubmissionStatus.ReuploadRequested => "Re-upload requested",
        _ => status.ToString()
    };

    private static string Humanise(TenantDocumentType type) => type switch
    {
        TenantDocumentType.RegistrationCertificate => "Registration certificate",
        TenantDocumentType.TaxExemptionCertificate => "Tax exemption certificate",
        TenantDocumentType.PanCard => "PAN card",
        TenantDocumentType.GstCertificate => "GST certificate",
        TenantDocumentType.AddressProof => "Address proof",
        TenantDocumentType.BankProof => "Bank proof",
        TenantDocumentType.TrustDeed => "Trust deed",
        TenantDocumentType.AnnualReport => "Annual report",
        TenantDocumentType.AuthorisedSignatoryProof => "Authorised signatory proof",
        TenantDocumentType.Logo => "Logo",
        _ => "Other"
    };
}
