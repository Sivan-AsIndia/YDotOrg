using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.Queries.DocumentSubmissions;

/// <summary>The limits the upload box must show and obey.</summary>
public sealed record GetDocumentUploadPolicyQuery;

/// <summary>Every submission belonging to the caller's own Organisation.</summary>
public sealed record GetMyDocumentSubmissionsQuery;

/// <summary>Every submission for one Organisation. Platform review path.</summary>
public sealed record GetOrganisationDocumentSubmissionsQuery(Guid TenantId);

/// <summary>
/// A short-lived link to one file.
///
/// <paramref name="Inline"/> chooses between rendering it in the review pane and saving it.
/// </summary>
public sealed record GetDocumentDownloadLinkQuery(Guid SubmissionId, Guid DocumentId, bool Inline);

/// <summary>
/// The read side of grouped document submissions.
///
/// THE DOWNLOAD LINK IS THE ONE THAT MATTERS HERE. A presigned URL is a bearer credential:
/// whoever holds it can fetch that object until it expires, with no further permission check.
/// So the permission is checked HERE, before one is minted, the access is written to the audit
/// trail, and the link is deliberately short-lived — the expiry is what limits the damage of a
/// URL pasted into a chat window.
/// </summary>
public sealed class DocumentSubmissionQueryHandler(
    ITenantRepository tenants,
    IUserRepository users,
    IObjectStorage storage,
    IAuditService audit,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IOptions<DocumentStorageSettings> storageOptions)
{
    private readonly DocumentStorageSettings _storage = storageOptions.Value;

    /// <summary>
    /// The limits, served rather than hard-coded in the client.
    ///
    /// This is what keeps "Maximum 5 MB" on the screen and the rule in the handler the same
    /// number. Anonymous callers never reach it; it is behind the ordinary authenticated
    /// surface, because there is no reason to publish a configuration to the internet.
    /// </summary>
    public Task<Result<DocumentUploadPolicyResponse>> HandleAsync(
        GetDocumentUploadPolicyQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new DocumentUploadPolicyResponse(
            _storage.MaximumFileSizeMegabytes,
            _storage.MaximumFileSizeBytes,
            _storage.MaximumFilesPerSubmission,
            _storage.AllowedContentTypes,
            DocumentContentTypes.ExtensionsFor(_storage.AllowedContentTypes),
            _storage.DownloadLinkExpirySeconds)));

    public async Task<Result<IReadOnlyList<DocumentSubmissionResponse>>> HandleAsync(
        GetMyDocumentSubmissionsQuery query, CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return Result.Failure<IReadOnlyList<DocumentSubmissionResponse>>(
                Error.TenantSelectionRequired());
        }

        return await ListAsync(tenantContext.RequireTenantId(), cancellationToken);
    }

    /// <summary>
    /// One Organisation's paperwork, as the PLATFORM REVIEWER sees it.
    /// </summary>
    /// <remarks>
    /// DRAFTS ARE EXCLUDED. A draft is work the organisation has not finished and has not sent;
    /// listing it beside a real submission put "Draft - Registration certificate - FILES 0 -
    /// SUBMITTED Not yet" on the reviewer's queue next to the actual evidence, which is an item of
    /// work that does not exist. The organisation still sees its own drafts on its own screen,
    /// because there it is the thing they are still filling in.
    /// </remarks>
    public async Task<Result<IReadOnlyList<DocumentSubmissionResponse>>> HandleAsync(
        GetOrganisationDocumentSubmissionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await ListAsync(query.TenantId, cancellationToken, includeDrafts: false);
    }

    /// <summary>
    /// Mints a download link, after checking that this caller may have one.
    ///
    /// THE ORDER IS THE POINT: permission, then audit, then the link. Writing the audit row
    /// after minting would leave a window where a link exists and no record says who asked for
    /// it, which is exactly the question a compliance review asks.
    /// </summary>
    public async Task<Result<DocumentDownloadLinkResponse>> HandleAsync(
        GetDocumentDownloadLinkQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var submission = await tenants.GetSubmissionAsync(query.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<DocumentDownloadLinkResponse>(
                Error.NotFound("That submission was not found."));
        }

        // A Tenant caller may only ever reach their own Organisation's paperwork. SuperAdmin
        // arrives through the platform route, which is gated on the review permission.
        if (!currentUser.IsSuperAdmin)
        {
            if (!tenantContext.HasTenant)
            {
                return Result.Failure<DocumentDownloadLinkResponse>(Error.TenantSelectionRequired());
            }

            if (submission.TenantId != tenantContext.RequireTenantId())
            {
                return Result.Failure<DocumentDownloadLinkResponse>(Error.CrossTenantAccessDenied(
                    "That document belongs to a different organisation."));
            }
        }

        var document = submission.Documents.FirstOrDefault(item => item.Id == query.DocumentId);
        if (document is null)
        {
            return Result.Failure<DocumentDownloadLinkResponse>(
                Error.NotFound("That file is not part of this submission."));
        }

        // Only what a browser can actually render is offered inline. Asking for an inline Word
        // document produces a download anyway, so the response says which it will be rather
        // than letting the screen promise a preview that never appears.
        var inline = query.Inline && DocumentContentTypes.IsPreviewable(document.ContentType);

        await audit.WriteAsync(
            AuditActionCodes.TenantDocumentDownloaded, nameof(TenantDocument), document.Id,
            document.FileName,
            new { submission.Id, submission.TenantId, Inline = inline, document.ContentHash },
            cancellationToken: cancellationToken);

        string url;

        try
        {
            url = await storage.GetDownloadUrlAsync(
                document.StoragePath, document.StorageVersionId, document.FileName, inline,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<DocumentDownloadLinkResponse>(Error.Dependency(
                "That file could not be opened just now. Try again in a moment."));
        }

        return Result.Success(new DocumentDownloadLinkResponse(
            document.Id,
            document.FileName,
            document.ContentType,
            document.FileSizeBytes,
            url,
            clock.UtcNow.AddSeconds(_storage.DownloadLinkExpirySeconds),
            DocumentContentTypes.IsPreviewable(document.ContentType)));
    }

    private async Task<Result<IReadOnlyList<DocumentSubmissionResponse>>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken, bool includeDrafts = true)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        var all = await tenants.GetSubmissionsAsync(tenantId, cancellationToken);

        var submissions = includeDrafts
            ? all
            : [.. all.Where(item => item.Status != TenantDocumentSubmissionStatus.Draft)];

        // Every name behind every submission, resolved in ONE query rather than one per row.
        var ids = submissions
            .SelectMany(submission => submission.Documents
                .Select(document => document.UploadedByUserId)
                .Append(submission.SubmittedByUserId)
                .Concat(submission.ReviewedByUserId.HasValue
                    ? [submission.ReviewedByUserId.Value]
                    : Array.Empty<Guid>()))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var names = await users.GetDisplayNamesAsync(ids, cancellationToken);

        IReadOnlyList<DocumentSubmissionResponse> result =
        [
            .. submissions.Select(submission =>
                // Reviewing means doing platform work. A root user standing inside an
                // Organisation is acting as that Organisation and gets its buttons.
                submission.ToResponse(
                    tenant, names, currentUser.IsSuperAdmin && !tenantContext.HasTenant))
        ];

        return Result.Success(result);
    }
}
