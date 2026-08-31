namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>What the store recorded about one object it just accepted.</summary>
/// <param name="StoragePath">The key the object lives under. Server-built, never client-supplied.</param>
/// <param name="VersionId">
/// The store's own version of this object. Versioning is switched on at the bucket, so writing
/// to the same key keeps the previous bytes rather than destroying them — which is what lets a
/// re-upload be reviewed against what it replaced instead of quietly overwriting the evidence.
/// </param>
/// <param name="SizeBytes">Size as the store actually received it, not as the client claimed.</param>
/// <param name="ContentHash">SHA-256 of the bytes, computed while streaming them in.</param>
public sealed record StoredObject(
    string StoragePath,
    string? VersionId,
    long SizeBytes,
    string ContentHash);

/// <summary>
/// The object store, as the rest of the application sees it.
///
/// THE FILES ARE NOT IN THE DATABASE. A row in <c>iam_tenant_documents</c> is metadata and a
/// pointer; the bytes live here. Putting a certificate in a table means every backup carries
/// megabytes of scanned PDFs and every query that touches the row risks dragging them along.
///
/// THIS INTERFACE IS DELIBERATELY SMALL. Four operations, no MinIO types in the signatures, no
/// S3 vocabulary leaking upward. Swapping MinIO for AWS S3 or Azure Blob means writing one new
/// class; nothing above this line changes, and nothing above this line may reference the client
/// library directly.
///
/// THE CALLER NEVER CHOOSES THE PATH. <see cref="BuildDocumentPath"/> derives it from the
/// Organisation and the document. Accepting a path from a caller — which is what the first
/// version of this feature did — lets a tenant write into another tenant's prefix by typing a
/// different string, and no amount of permission checking elsewhere repairs that.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Creates the bucket if it is missing and switches versioning on. Called once at startup;
    /// safe to call again.
    /// </summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams one file in, hashing it on the way through.
    ///
    /// The hash is computed here rather than trusted from the client, so it describes what was
    /// actually stored. A client-supplied hash only proves what the client meant to send.
    /// </summary>
    Task<StoredObject> PutAsync(
        string storagePath,
        Stream content,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken);

    /// <summary>
    /// A time-limited URL the browser can fetch the object from directly.
    ///
    /// The bytes bypass this API entirely, which is the point: a reviewer opening a 4 MB scan
    /// should not occupy an application thread for the duration. The link is a bearer
    /// credential, so it expires in minutes and is only ever issued after the caller's
    /// permission has been checked and the access recorded.
    /// </summary>
    Task<string> GetDownloadUrlAsync(
        string storagePath,
        string? versionId,
        string downloadFileName,
        bool inline,
        CancellationToken cancellationToken);

    /// <summary>Removes an object. Used only when an upload half-succeeded and left an orphan.</summary>
    Task RemoveAsync(string storagePath, string? versionId, CancellationToken cancellationToken);

    /// <summary>
    /// The key one document lives under.
    ///
    /// Keyed by Organisation first, so a bucket policy or an audit can be scoped to one
    /// Organisation by prefix, and so nothing belonging to two Organisations ever shares a
    /// folder. The file name is slugified rather than passed through: a name containing "../"
    /// is a path-traversal attempt, and a name containing a space is merely annoying.
    /// </summary>
    static string BuildDocumentPath(Guid tenantId, Guid submissionId, Guid documentId, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var safeExtension = string.IsNullOrWhiteSpace(extension) || extension.Length > 12
            ? string.Empty
            : new string([.. extension.Where(character =>
                char.IsLetterOrDigit(character) || character == '.')]);

        return $"organisations/{tenantId:D}/submissions/{submissionId:D}/{documentId:D}{safeExtension}";
    }
}
