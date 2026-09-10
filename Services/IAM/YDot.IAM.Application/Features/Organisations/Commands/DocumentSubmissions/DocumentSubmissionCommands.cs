using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.Commands.DocumentSubmissions;

/// <summary>Opens a Draft submission for the caller's own Organisation.</summary>
public sealed record CreateDocumentSubmissionCommand(CreateDocumentSubmissionRequest Request);

/// <summary>
/// Attaches one file to a Draft submission.
///
/// The stream is the request body, handed straight to the object store. It is never buffered
/// into a string or a byte array on the way through: a 5 MB base64 blob in a JSON envelope is
/// how the first version of this feature ended up putting scanned certificates into the request
/// log.
/// </summary>
public sealed record UploadSubmissionFileCommand(
    Guid SubmissionId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

/// <summary>Removes a file from a submission the Organisation may still edit.</summary>
public sealed record RemoveSubmissionFileCommand(Guid SubmissionId, Guid DocumentId);

/// <summary>
/// Withdraws a draft submission the organisation has decided against.
///
/// IT EXISTS BECAUSE THERE WAS NO WAY OUT. A draft card offered a file upload and nothing else -
/// no delete, no cancel, no discard - so an submission opened by mistake was permanent, and an
/// empty one sat on the organisation's own screen for ever.
/// </summary>
public sealed record DiscardDocumentSubmissionCommand(Guid SubmissionId, long ExpectedVersion);

/// <summary>Hands the submission to the reviewers.</summary>
public sealed record SubmitDocumentSubmissionCommand(Guid SubmissionId, SubmitDocumentSubmissionRequest Request);

/// <summary>A reviewer picks it up, so the queue shows somebody is on it.</summary>
public sealed record StartDocumentSubmissionReviewCommand(Guid SubmissionId);

/// <summary>Approve, reject, or ask for a better copy.</summary>
public sealed record DecideDocumentSubmissionCommand(Guid SubmissionId, DecideDocumentSubmissionRequest Request);

/// <summary>
/// The write side of grouped document submissions.
///
/// WHERE THE ORGANISATION COMES FROM IS THE SECURITY MODEL, and it differs between the two
/// audiences on purpose:
///
/// <code>
/// TenantAdmin   from the request context. There is no id in the URL, so there is nothing
///               to change in order to reach somebody else's paperwork.
/// SuperAdmin    from the submission itself, reachable only with the platform review
///               permission. They are not "in" the Organisation and do not need to be.
/// </code>
///
/// EVERY TRANSITION GOES THROUGH THE STATE MACHINE on
/// <see cref="TenantDocumentSubmission.AllowedTransitionsFrom"/>. No handler here sets
/// <c>Status</c> without asking first, which is what stops a submission being approved before it
/// was ever submitted.
/// </summary>
public sealed class DocumentSubmissionCommandHandler(
    ITenantRepository tenants,
    IUserRepository users,
    IObjectStorage storage,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IOptions<DocumentStorageSettings> storageOptions,
    ILogger<DocumentSubmissionCommandHandler> logger)
{
    private readonly DocumentStorageSettings _storage = storageOptions.Value;

    // =============================================================================================
    // The Organisation's side
    // =============================================================================================

    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        CreateDocumentSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Creating document submission. UserId: {UserId}.", currentUser.UserId);

        if (!tenantContext.HasTenant)
        {
            logger.LogWarning("Document submission creation rejected because tenant selection is required. UserId: {UserId}.", currentUser.UserId);
            return Result.Failure<DocumentSubmissionResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Document submission creation failed because the selected tenant was not found. UserId: {UserId}, TenantId: {TenantId}.", currentUser.UserId, tenantId);
            return Result.Failure<DocumentSubmissionResponse>(Error.TenantNotFound());
        }

        // THE DOCUMENT SET CLOSES ON APPROVAL. Nothing enforced this: an Organisation that
        // SuperAdmin had already accepted could open a new submission and upload registration
        // evidence to it indefinitely, which changes what the approval was granted against with
        // nobody reviewing the new file. See Tenant.AcceptsDocumentSubmissions.
        if (!tenant.AcceptsDocumentSubmissions)
        {
            logger.LogWarning("Document submission creation rejected because the tenant no longer accepts document submissions. UserId: {UserId}, TenantId: {TenantId}.", currentUser.UserId, tenantId);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                "This Organisation's registration has already been decided, so its documents are "
                + "settled. A new submission cannot be started."));
        }

        var submission = new TenantDocumentSubmission
        {
            TenantId = tenantId,
            BusinessUnitId = tenant.BusinessUnitId,
            DocumentType = command.Request.DocumentType,
            Title = command.Request.Title?.Trim(),
            Notes = command.Request.Notes?.Trim(),
            Status = TenantDocumentSubmissionStatus.Draft,
            SubmittedByUserId = currentUser.UserId
        };

        await tenants.AddSubmissionAsync(submission, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.DocumentSubmissionCreated, nameof(TenantDocumentSubmission),
            submission.Id, submission.Title ?? submission.DocumentType.ToString(),
            new { submission.DocumentType }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Document submission created successfully. UserId: {UserId}, TenantId: {TenantId}, SubmissionId: {SubmissionId}.", currentUser.UserId, tenantId, submission.Id);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    /// <summary>
    /// Stores one file and records it against the submission.
    ///
    /// THE VALIDATION ORDER MATTERS. Size and type are checked BEFORE a single byte reaches the
    /// object store, so a refused upload costs nothing and leaves nothing behind to clean up.
    /// </summary>
    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        UploadSubmissionFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Uploading document submission file. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            logger.LogWarning("Document submission file upload failed because the submission was not found. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That submission was not found."));
        }

        var owned = EnsureOwnedByCaller(submission);
        if (owned is not null)
        {
            logger.LogWarning("Document submission file upload rejected due to ownership or tenant access rules. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);
            return Result.Failure<DocumentSubmissionResponse>(owned);
        }

        if (!submission.IsEditable)
        {
            logger.LogWarning("Document submission file upload rejected because the submission is not editable. UserId: {UserId}, SubmissionId: {SubmissionId}, Status: {Status}.", currentUser.UserId, submission.Id, submission.Status);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                "This submission is with the reviewers and cannot be changed. "
                + "Wait for the outcome, or start a new submission."));
        }

        if (submission.Documents.Count >= _storage.MaximumFilesPerSubmission)
        {
            logger.LogWarning("Document submission file upload rejected because the maximum file count was reached. UserId: {UserId}, SubmissionId: {SubmissionId}, FileCount: {FileCount}.", currentUser.UserId, submission.Id, submission.Documents.Count);
            return Result.Failure<DocumentSubmissionResponse>(Error.Validation(
                $"A submission may carry at most {_storage.MaximumFilesPerSubmission} files.",
                [new ValidationError("Files", "Remove a file before adding another.")]));
        }

        var invalid = ValidateFile(command.FileName, command.ContentType, command.SizeBytes);
        if (invalid is not null)
        {
            logger.LogWarning("Document submission file upload rejected by file validation. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.Id);
            return Result.Failure<DocumentSubmissionResponse>(invalid);
        }

        var tenant = await tenants.GetByIdAsync(submission.TenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Document submission file upload failed because the tenant was not found. UserId: {UserId}, TenantId: {TenantId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.TenantId, submission.Id);
            return Result.Failure<DocumentSubmissionResponse>(Error.TenantNotFound());
        }

        // The same close-on-approval rule as the create above, applied to the file rather than
        // the submission: a draft left open from before the decision must not become a way to
        // add evidence after it.
        if (!tenant.AcceptsDocumentSubmissions)
        {
            logger.LogWarning("Document submission file upload rejected because the tenant no longer accepts document submissions. UserId: {UserId}, TenantId: {TenantId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.TenantId, submission.Id);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                "This Organisation's registration has already been decided, so its documents are "
                + "settled. No further files can be added."));
        }

        var documentId = Guid.NewGuid();

        // THE PATH IS DERIVED, NOT ACCEPTED. A caller-supplied storage path lets one Organisation
        // write into another's prefix by typing a different string.
        var storagePath = IObjectStorage.BuildDocumentPath(
            submission.TenantId, submission.Id, documentId, command.FileName);

        StoredObject stored;

        try
        {
            stored = await storage.PutAsync(
                storagePath, command.Content, command.ContentType, command.SizeBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Document submission file could not be stored. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.Id);
            return Result.Failure<DocumentSubmissionResponse>(Error.Dependency(
                "The file could not be stored. Try again in a moment."));
        }

        // The store reports what it actually received. A client that understated the size in the
        // form part does not get to smuggle a larger file past the check above.
        if (stored.SizeBytes > _storage.MaximumFileSizeBytes)
        {
            logger.LogWarning("Document submission file upload rejected because the stored file exceeded the configured size limit. UserId: {UserId}, SubmissionId: {SubmissionId}, SizeBytes: {SizeBytes}.", currentUser.UserId, submission.Id, stored.SizeBytes);
            await SafeRemoveAsync(stored, cancellationToken);

            return Result.Failure<DocumentSubmissionResponse>(Error.Validation(
                $"That file is larger than {_storage.MaximumFileSizeMegabytes} MB.",
                [new ValidationError("File", "Choose a smaller file.")]));
        }

        var now = clock.UtcNow;

        var document = new TenantDocument
        {
            Id = documentId,
            TenantId = submission.TenantId,
            BusinessUnitId = tenant.BusinessUnitId,
            SubmissionId = submission.Id,
            DocumentType = submission.DocumentType,
            FileName = command.FileName,
            StoragePath = stored.StoragePath,
            StorageVersionId = stored.VersionId,
            ContentType = command.ContentType,
            FileSizeBytes = stored.SizeBytes,
            ContentHash = stored.ContentHash,
            Status = TenantDocumentStatus.Uploaded,
            UploadedAtUtc = now,
            UploadedByUserId = currentUser.UserId
        };

        await tenants.AddDocumentAsync(document, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantDocumentUploaded, nameof(TenantDocument), document.Id,
            document.FileName,
            new { submission.Id, document.FileSizeBytes, document.ContentHash, document.ContentType },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // NOT added to submission.Documents by hand. EF's change tracker fixes the navigation
        // up when the document is saved, so doing it here too listed every upload twice - a
        // one-file submission reported "2 files" and the reviewer saw the same PDF written out
        // twice under it.

        logger.LogInformation("Document submission file uploaded successfully. UserId: {UserId}, SubmissionId: {SubmissionId}, DocumentId: {DocumentId}, SizeBytes: {SizeBytes}.", currentUser.UserId, submission.Id, document.Id, document.FileSizeBytes);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        RemoveSubmissionFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Removing document submission file. UserId: {UserId}, SubmissionId: {SubmissionId}, DocumentId: {DocumentId}.", currentUser.UserId, command.SubmissionId, command.DocumentId);

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That submission was not found."));
        }

        var owned = EnsureOwnedByCaller(submission);
        if (owned is not null)
        {
            return Result.Failure<DocumentSubmissionResponse>(owned);
        }

        if (!submission.IsEditable)
        {
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                "This submission is with the reviewers and cannot be changed."));
        }

        var document = submission.Documents.FirstOrDefault(item => item.Id == command.DocumentId);
        if (document is null)
        {
            logger.LogWarning("Document submission file removal failed because the document was not found in the submission. UserId: {UserId}, SubmissionId: {SubmissionId}, DocumentId: {DocumentId}.", currentUser.UserId, submission.Id, command.DocumentId);
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That file is not part of this submission."));
        }

        // THE OBJECT IS LEFT IN THE STORE ON PURPOSE. Versioning keeps it, and a file that was
        // attached and withdrawn before submission is part of the trail. Storage is cheap; an
        // audit with holes in it is not.
        tenants.RemoveDocument(document);
        submission.Documents.Remove(document);

        await audit.WriteAsync(
            AuditActionCodes.DocumentSubmissionFileRemoved, nameof(TenantDocument), document.Id,
            document.FileName, new { submission.Id }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await tenants.GetByIdAsync(submission.TenantId, cancellationToken);

        logger.LogInformation("Document submission file removed successfully. UserId: {UserId}, SubmissionId: {SubmissionId}, DocumentId: {DocumentId}.", currentUser.UserId, submission.Id, document.Id);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    /// <summary>
    /// Discards an unsent draft.
    /// </summary>
    /// <remarks>
    /// ONLY A DRAFT, AND ONLY THE ORGANISATION'S OWN. Once a submission has been sent it is
    /// evidence somebody is deciding on, and withdrawing it from under a reviewer is not
    /// something a screen should be able to do - the server refuses it here rather than trusting
    /// the button to be hidden.
    ///
    /// THE FILES GO WITH IT, and only in this one case. Elsewhere a removed file is kept, because
    /// attaching and withdrawing it before submission is part of the trail; a draft that is being
    /// discarded entirely has no trail to be part of, and keeping orphaned rows against a
    /// submission that no longer exists would leave the organisation's paperwork screen showing
    /// files belonging to nothing. The audit row records what was discarded and by whom.
    /// </remarks>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DiscardDocumentSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Discarding document submission draft. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That submission was not found."));
        }

        var owned = EnsureOwnedByCaller(submission);
        if (owned is not null)
        {
            return Result.Failure<OutcomeResponse>(owned);
        }

        if (submission.Status != TenantDocumentSubmissionStatus.Draft)
        {
            logger.LogWarning("Document submission draft discard rejected because the submission is not a draft. UserId: {UserId}, SubmissionId: {SubmissionId}, Status: {Status}.", currentUser.UserId, submission.Id, submission.Status);
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Only a draft can be discarded. This submission has already been sent for review."));
        }

        var fileCount = submission.Documents.Count;
        var documentType = submission.DocumentType;

        foreach (var document in submission.Documents.ToList())
        {
            tenants.RemoveDocument(document);
        }

        submission.Documents.Clear();
        tenants.RemoveSubmission(submission);

        await audit.WriteAsync(
            AuditActionCodes.DocumentSubmissionDiscarded,
            nameof(TenantDocumentSubmission),
            submission.Id,
            documentType.ToString(),
            new { FileCount = fileCount },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Document submission draft discarded successfully. UserId: {UserId}, SubmissionId: {SubmissionId}, FileCount: {FileCount}.", currentUser.UserId, submission.Id, fileCount);

        return Result.Success(new OutcomeResponse(
            command.SubmissionId,
            TenantDocumentSubmissionStatus.Draft.ToString(),
            command.ExpectedVersion,
            "The draft was discarded.",
            []));
    }

    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        SubmitDocumentSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Submitting document submission for review. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That submission was not found."));
        }

        var owned = EnsureOwnedByCaller(submission);
        if (owned is not null)
        {
            return Result.Failure<DocumentSubmissionResponse>(owned);
        }

        if (!submission.CanTransitionTo(TenantDocumentSubmissionStatus.Submitted))
        {
            logger.LogWarning("Document submission rejected because its current status cannot transition to Submitted. UserId: {UserId}, SubmissionId: {SubmissionId}, Status: {Status}.", currentUser.UserId, submission.Id, submission.Status);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                $"A submission that is {submission.Status} cannot be sent for review."));
        }

        // AN EMPTY SUBMISSION IS THE WHOLE PROBLEM THIS FEATURE EXISTS TO FIX. A reviewer opening
        // one with nothing in it is being asked to verify a registration against no evidence.
        if (submission.Documents.Count == 0)
        {
            logger.LogWarning("Document submission rejected because no files are attached. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.Id);
            return Result.Failure<DocumentSubmissionResponse>(Error.Validation(
                "Attach at least one file before sending this for review.",
                [new ValidationError("Files", "A submission must contain at least one file.")]));
        }

        var wasSentBack = submission.Status == TenantDocumentSubmissionStatus.ReuploadRequested;

        submission.Status = TenantDocumentSubmissionStatus.Submitted;
        submission.SubmittedAtUtc = clock.UtcNow;
        submission.SubmittedByUserId = currentUser.UserId;

        if (wasSentBack)
        {
            submission.ReuploadCount += 1;

            // The old instruction is cleared, so the reviewer does not see a stale "send a
            // clearer scan" beside the clearer scan.
            submission.DecisionNotes = null;
        }

        if (!string.IsNullOrWhiteSpace(command.Request.Notes))
        {
            submission.Notes = command.Request.Notes.Trim();
        }

        await audit.WriteAsync(
            AuditActionCodes.DocumentSubmissionSubmitted, nameof(TenantDocumentSubmission),
            submission.Id, submission.Title ?? submission.DocumentType.ToString(),
            new { FileCount = submission.Documents.Count, submission.ReuploadCount },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await tenants.GetByIdAsync(submission.TenantId, cancellationToken);

        logger.LogInformation("Document submission submitted successfully. UserId: {UserId}, SubmissionId: {SubmissionId}, FileCount: {FileCount}, ReuploadCount: {ReuploadCount}.", currentUser.UserId, submission.Id, submission.Documents.Count, submission.ReuploadCount);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    // =============================================================================================
    // The reviewer's side
    // =============================================================================================

    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        StartDocumentSubmissionReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Starting document submission review. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, command.SubmissionId);

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That submission was not found."));
        }

        if (!submission.CanTransitionTo(TenantDocumentSubmissionStatus.UnderReview))
        {
            logger.LogWarning("Document submission review start rejected because its current status cannot transition to UnderReview. UserId: {UserId}, SubmissionId: {SubmissionId}, Status: {Status}.", currentUser.UserId, submission.Id, submission.Status);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                $"A submission that is {submission.Status} cannot be picked up for review."));
        }

        submission.Status = TenantDocumentSubmissionStatus.UnderReview;
        submission.ReviewStartedAtUtc = clock.UtcNow;
        submission.ReviewedByUserId = currentUser.UserId;

        await audit.WriteAsync(
            AuditActionCodes.DocumentSubmissionReviewStarted, nameof(TenantDocumentSubmission),
            submission.Id, submission.Title ?? submission.DocumentType.ToString(),
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await tenants.GetByIdAsync(submission.TenantId, cancellationToken);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    /// <summary>
    /// The decision.
    ///
    /// A REASON IS MANDATORY FOR ANYTHING BUT AN APPROVAL, checked here as well as at the
    /// database, because "rejected" with no explanation leaves the Organisation guessing at what
    /// to change and guarantees a second rejection.
    /// </summary>
    public async Task<Result<DocumentSubmissionResponse>> HandleAsync(
        DecideDocumentSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        logger.LogInformation("Deciding document submission. UserId: {UserId}, SubmissionId: {SubmissionId}, Decision: {Decision}.", currentUser.UserId, command.SubmissionId, request.Decision);

        if (request.Decision != DocumentSubmissionDecision.Approve
            && string.IsNullOrWhiteSpace(request.Notes))
        {
            logger.LogWarning("Document submission decision rejected because a reason is required. UserId: {UserId}, SubmissionId: {SubmissionId}, Decision: {Decision}.", currentUser.UserId, command.SubmissionId, request.Decision);
            return Result.Failure<DocumentSubmissionResponse>(Error.Validation(
                request.Decision == DocumentSubmissionDecision.Reject
                    ? "Give a reason so the organisation can correct and resubmit."
                    : "Say what is wrong with the file, so a better copy can be sent.",
                [new ValidationError(nameof(request.Notes), "A reason is required.")]));
        }

        var submission = await tenants.GetSubmissionAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<DocumentSubmissionResponse>(
                Error.NotFound("That submission was not found."));
        }

        var target = request.Decision switch
        {
            DocumentSubmissionDecision.Approve => TenantDocumentSubmissionStatus.Approved,
            DocumentSubmissionDecision.Reject => TenantDocumentSubmissionStatus.Rejected,
            _ => TenantDocumentSubmissionStatus.ReuploadRequested
        };

        if (!submission.CanTransitionTo(target))
        {
            logger.LogWarning("Document submission decision rejected because the current status cannot transition to the requested target. UserId: {UserId}, SubmissionId: {SubmissionId}, Status: {Status}, Target: {Target}.", currentUser.UserId, submission.Id, submission.Status, target);
            return Result.Failure<DocumentSubmissionResponse>(Error.InvalidTransition(
                $"A submission that is {submission.Status} cannot be moved to {target}."));
        }

        var now = clock.UtcNow;

        submission.Status = target;
        submission.ReviewedByUserId = currentUser.UserId;
        submission.DecisionNotes = request.Notes?.Trim();

        // Only a final decision is "decided". A send-back is a pause, and stamping it as decided
        // would make the queue's age column start again from the wrong moment.
        submission.DecidedAtUtc = target == TenantDocumentSubmissionStatus.ReuploadRequested ? null : now;

        // The files carry the decision too, so a document opened on its own still shows what
        // was concluded about it.
        var fileStatus = target switch
        {
            TenantDocumentSubmissionStatus.Approved => TenantDocumentStatus.Accepted,
            TenantDocumentSubmissionStatus.Rejected => TenantDocumentStatus.Rejected,
            _ => TenantDocumentStatus.UnderReview
        };

        foreach (var document in submission.Documents)
        {
            document.Status = fileStatus;
            document.ReviewedAtUtc = now;
            document.ReviewedByUserId = currentUser.UserId;

            // The check constraint refuses a rejected row with no notes, and it is right to.
            if (fileStatus == TenantDocumentStatus.Rejected)
            {
                document.ReviewNotes = submission.DecisionNotes;
            }
        }

        var actionCode = target switch
        {
            TenantDocumentSubmissionStatus.Approved => AuditActionCodes.DocumentSubmissionApproved,
            TenantDocumentSubmissionStatus.Rejected => AuditActionCodes.DocumentSubmissionRejected,
            _ => AuditActionCodes.DocumentSubmissionReuploadRequested
        };

        await audit.WriteAsync(
            actionCode, nameof(TenantDocumentSubmission), submission.Id,
            submission.Title ?? submission.DocumentType.ToString(),
            new { Decision = target.ToString(), FileCount = submission.Documents.Count },
            submission.DecisionNotes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await tenants.GetByIdAsync(submission.TenantId, cancellationToken);

        logger.LogInformation("Document submission decision completed successfully. UserId: {UserId}, SubmissionId: {SubmissionId}, Target: {Target}, FileCount: {FileCount}.", currentUser.UserId, submission.Id, target, submission.Documents.Count);

        return Result.Success(await DescribeAsync(submission, tenant, cancellationToken));
    }

    // =============================================================================================
    // Shared
    // =============================================================================================

    /// <summary>
    /// Refuses a Tenant caller reaching a submission that is not their Organisation's.
    ///
    /// SuperAdmin is exempt: they reach these through the platform route, which is already
    /// gated on the review permission, and they are not "in" the Organisation.
    /// </summary>
    private Error? EnsureOwnedByCaller(TenantDocumentSubmission submission)
    {
        if (currentUser.IsSuperAdmin)
        {
            return null;
        }

        if (!tenantContext.HasTenant)
        {
            logger.LogWarning("Document submission access rejected because tenant selection is required. UserId: {UserId}, SubmissionId: {SubmissionId}.", currentUser.UserId, submission.Id);
            return Error.TenantSelectionRequired();
        }

        return submission.TenantId == tenantContext.RequireTenantId()
            ? null
            : LogCrossTenantAccessDenied(submission);
    }

    private Error LogCrossTenantAccessDenied(TenantDocumentSubmission submission)
    {
        logger.LogWarning("Document submission access rejected because the submission belongs to a different tenant. UserId: {UserId}, SubmissionId: {SubmissionId}, TenantId: {TenantId}.", currentUser.UserId, submission.Id, submission.TenantId);
        return Error.CrossTenantAccessDenied("That submission belongs to a different organisation.");
    }

    /// <summary>
    /// Size and type, checked before anything is stored.
    ///
    /// THE EXTENSION HAS TO AGREE WITH THE DECLARED TYPE. A browser reports the content type
    /// from the file's extension, so a caller posting the form by hand can claim anything.
    /// Requiring both to line up means a ".exe" announced as "application/pdf" is refused.
    /// </summary>
    private Error? ValidateFile(string fileName, string contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            logger.LogWarning("Document file validation failed because the file name is missing.");
            return Error.Validation("The file has no name.",
                [new ValidationError("File", "Choose a file.")]);
        }

        if (sizeBytes <= 0)
        {
            logger.LogWarning("Document file validation failed because the file is empty.");
            return Error.Validation("That file is empty.",
                [new ValidationError("File", "Choose a file with content in it.")]);
        }

        if (sizeBytes > _storage.MaximumFileSizeBytes)
        {
            logger.LogWarning("Document file validation failed because the declared size exceeds the configured limit. SizeBytes: {SizeBytes}.", sizeBytes);
            return Error.Validation(
                $"That file is {sizeBytes / 1024d / 1024d:0.0} MB. "
                + $"The limit is {_storage.MaximumFileSizeMegabytes} MB.",
                [new ValidationError("File", "Upload a smaller copy.")]);
        }

        if (!_storage.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogWarning("Document file validation failed because the content type is not allowed. ContentType: {ContentType}.", contentType);
            return Error.Validation(
                "That kind of file cannot be uploaded. Use a PDF, an image, or an Office document.",
                [new ValidationError("File", $"{contentType} is not accepted.")]);
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!DocumentContentTypes.ExtensionMatches(contentType, extension))
        {
            logger.LogWarning("Document file validation failed because the file extension does not match the declared content type. ContentType: {ContentType}, Extension: {Extension}.", contentType, extension);
            return Error.Validation(
                $"The file name ends in \"{extension}\", which does not match its contents.",
                [new ValidationError("File", "Rename the file correctly, or choose another.")]);
        }

        return null;
    }

    private async Task SafeRemoveAsync(StoredObject stored, CancellationToken cancellationToken)
    {
        try
        {
            await storage.RemoveAsync(stored.StoragePath, stored.VersionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to remove an orphaned stored document object during cleanup.");
            // An orphaned object is untidy; failing the request twice helps nobody.
        }
    }

    /// <summary>Builds the response, resolving the names behind the user ids.</summary>
    private async Task<DocumentSubmissionResponse> DescribeAsync(
        TenantDocumentSubmission submission, Tenant? tenant, CancellationToken cancellationToken)
    {
        var names = await ResolveNamesAsync(submission, cancellationToken);

        return submission.ToResponse(tenant, names, IsActingAsReviewer);
    }

    /// <summary>
    /// Whether this caller is REVIEWING rather than assembling a submission.
    ///
    /// Being SuperAdmin is not enough on its own. A root user who has entered an Organisation
    /// from the switcher is acting AS that Organisation - the same reasoning the navigation
    /// builder uses to hide the platform branch from them while they are inside one - so they
    /// should be offered the Organisation's buttons, not a reviewer's. Testing this caught it:
    /// a submission in Draft came back with no actions at all, because the reviewer list is
    /// empty for a Draft and the caller had been handed the wrong list.
    /// </summary>
    private bool IsActingAsReviewer => currentUser.IsSuperAdmin && !tenantContext.HasTenant;

    /// <summary>
    /// Display names for everybody involved, in one lookup.
    ///
    /// A screen that shows "uploaded by 8f2c…" is showing a database key to a person. The ids
    /// are gathered first and resolved together, rather than a query per row.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        TenantDocumentSubmission submission, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid> { submission.SubmittedByUserId };

        if (submission.ReviewedByUserId.HasValue)
        {
            ids.Add(submission.ReviewedByUserId.Value);
        }

        foreach (var document in submission.Documents)
        {
            ids.Add(document.UploadedByUserId);
        }

        ids.Remove(Guid.Empty);

        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(ids, cancellationToken);
    }
}
