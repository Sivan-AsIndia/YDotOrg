using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.Organisations.Commands.DocumentSubmissions;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.Queries.DocumentSubmissions;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Grouped document submissions — the Organisation's side and the reviewer's side.
///
/// TWO HALVES WITH DIFFERENT AUDIENCES, and the split is the security model:
///
/// <code>
/// /api/v1/organisations/mine/document-submissions        TENANT.   No id in the URL, so the
///                                                                  Organisation comes from the
///                                                                  request context and there is
///                                                                  nothing to tamper with.
/// /api/v1/organisations/{id}/document-submissions        PLATFORM. SuperAdmin reviewing, gated
///                                                                  on the platform review
///                                                                  permission.
/// </code>
///
/// THE UPLOAD ENDPOINT TAKES MULTIPART, NOT JSON. A file posted as base64 inside a JSON envelope
/// costs a third more bandwidth, cannot be streamed, and ends up in the request log. This one
/// hands the request stream straight to the object store.
/// </summary>
[Route("api/v1/organisations")]
[Authorize]
public sealed class OrganisationDocumentsController(
    DocumentSubmissionCommandHandler commands,
    DocumentSubmissionQueryHandler queries) : ApiControllerBase
{
    // =================================================================================
    // Shared: what may be uploaded
    // =================================================================================

    /// <summary>
    /// The limits the upload box must show and obey.
    ///
    /// Served rather than hard-coded in the client so the sentence on the screen and the rule in
    /// the handler are the same number. Allowed during onboarding, because completing the
    /// profile is exactly when it is needed.
    /// </summary>
    [HttpGet("document-upload-policy")]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<DocumentUploadPolicyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUploadPolicyAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetDocumentUploadPolicyQuery(), cancellationToken));

    // =================================================================================
    // Tenant: the Organisation assembling and sending its own paperwork
    // =================================================================================

    [HttpGet("mine/document-submissions")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DocumentSubmissionResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMineAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetMyDocumentSubmissionsQuery(), cancellationToken));

    /// <summary>Opens a Draft submission. Files are attached afterwards, one call each.</summary>
    [HttpPost("mine/document-submissions")]
    [HasPermission(PermissionCodes.OrganisationUploadDocument)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateMineAsync(
        [FromBody] CreateDocumentSubmissionRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(new CreateDocumentSubmissionCommand(request), cancellationToken),
            "Submission started. Attach your files, then send it for review.");

    /// <summary>
    /// Attaches one file.
    ///
    /// <c>RequestSizeLimit</c> is a HARD CEILING well above the configured limit, not the limit
    /// itself — the real check is in the handler, where the number comes from configuration and
    /// the message can explain itself. This one exists so a caller cannot occupy the server by
    /// streaming gigabytes at an endpoint that was going to refuse them anyway.
    /// </summary>
    [HttpPost("mine/document-submissions/{submissionId:guid}/files")]
    [HasPermission(PermissionCodes.OrganisationUploadDocument)]
    [AllowedWhileOnboarding]
    [RequestSizeLimit(64 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFileAsync(
        Guid submissionId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return FromResult(Result.Failure<DocumentSubmissionResponse>(Error.Validation(
                "No file was received.",
                [new ValidationError("File", "Choose a file to upload.")])));
        }

        await using var content = file.OpenReadStream();

        return FromResult(await commands.HandleAsync(
            new UploadSubmissionFileCommand(
                submissionId,
                // The browser sends the name in quotes on some platforms, and a path on others.
                Path.GetFileName(file.FileName?.Trim('"') ?? "document"),
                file.ContentType ?? "application/octet-stream",
                file.Length,
                content),
            cancellationToken));
    }

    [HttpDelete("mine/document-submissions/{submissionId:guid}/files/{documentId:guid}")]
    [HasPermission(PermissionCodes.OrganisationUploadDocument)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveFileAsync(
        Guid submissionId, Guid documentId, CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(
                new RemoveSubmissionFileCommand(submissionId, documentId), cancellationToken),
            "File removed.");

    /// <summary>
    /// Discards a draft submission the organisation has decided against.
    ///
    /// THE SAME PERMISSION AS ATTACHING A FILE, because it is the same act of managing one's own
    /// unsent paperwork. The handler refuses anything that is not still a draft, so this cannot
    /// pull a submission out from under a reviewer.
    /// </summary>
    [HttpDelete("mine/document-submissions/{submissionId:guid}")]
    [HasPermission(PermissionCodes.OrganisationUploadDocument)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardMineAsync(
        Guid submissionId,
        [FromQuery] long expectedVersion,
        CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(
                new DiscardDocumentSubmissionCommand(submissionId, expectedVersion),
                cancellationToken),
            "Draft discarded.");

    [HttpPost("mine/document-submissions/{submissionId:guid}/submit")]
    [HasPermission(PermissionCodes.OrganisationSubmit)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitMineAsync(
        Guid submissionId,
        [FromBody] SubmitDocumentSubmissionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(
                new SubmitDocumentSubmissionCommand(submissionId, request), cancellationToken),
            "Sent for review. You will be told the outcome.");

    /// <summary>
    /// A short-lived link to one of the caller's own files.
    ///
    /// The Organisation can re-open what it sent, which is how somebody checks they attached the
    /// right scan before chasing a reviewer about it.
    /// </summary>
    [HttpGet("mine/document-submissions/{submissionId:guid}/files/{documentId:guid}/link")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<DocumentDownloadLinkResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyFileLinkAsync(
        Guid submissionId, Guid documentId, [FromQuery] bool inline,
        CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetDocumentDownloadLinkQuery(submissionId, documentId, inline), cancellationToken));

    // =================================================================================
    // Platform: SuperAdmin reviewing
    // =================================================================================

    [HttpGet("{tenantId:guid}/document-submissions")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DocumentSubmissionResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForOrganisationAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetOrganisationDocumentSubmissionsQuery(tenantId), cancellationToken));

    /// <summary>
    /// A link for the reviewer.
    ///
    /// <c>inline=true</c> is what lets a PDF or an image render inside the review pane instead
    /// of landing in the downloads folder. Every issue is recorded in the audit trail before the
    /// link is minted, because after that point the link answers to nobody.
    /// </summary>
    [HttpGet("{tenantId:guid}/document-submissions/{submissionId:guid}/files/{documentId:guid}/link")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<DocumentDownloadLinkResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewFileLinkAsync(
        Guid tenantId, Guid submissionId, Guid documentId, [FromQuery] bool inline,
        CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetDocumentDownloadLinkQuery(submissionId, documentId, inline), cancellationToken));

    [HttpPost("{tenantId:guid}/document-submissions/{submissionId:guid}/start-review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartReviewAsync(
        Guid tenantId, Guid submissionId, CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(
                new StartDocumentSubmissionReviewCommand(submissionId), cancellationToken),
            "Review started.");

    /// <summary>Approve, reject, or ask for a better copy. A reason is required for the last two.</summary>
    [HttpPost("{tenantId:guid}/document-submissions/{submissionId:guid}/decide")]
    [HasPermission(PermissionCodes.Platform.TenantsApprove)]
    [ProducesResponseType(typeof(ApiResponse<DocumentSubmissionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DecideAsync(
        Guid tenantId, Guid submissionId,
        [FromBody] DecideDocumentSubmissionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new DecideDocumentSubmissionCommand(submissionId, request), cancellationToken));
}
