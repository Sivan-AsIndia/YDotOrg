namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Where Organisation documents are stored, and what may be put there.
///
/// EVERY LIMIT IS CONFIGURATION, NOT CODE. A file-size cap or an accepted content type that
/// only exists as a constant somewhere means a deployment cannot change it without a release,
/// and the two ends then disagree the moment somebody edits one of them. The client reads
/// these same numbers back from the API, so the message a person sees before they choose a
/// file is the rule the server will actually apply.
///
/// S3-COMPATIBLE, NOT MINIO-SPECIFIC. MinIO speaks the S3 API, so pointing
/// <see cref="Endpoint"/> at AWS S3 or any other gateway is a configuration change and nothing
/// more. That is the whole reason the abstraction exists.
/// </summary>
public sealed class DocumentStorageSettings
{
    public const string SectionName = "DocumentStorageSettings";

    /// <summary>Host and port only — "ydot-minio:9000" — never a scheme.</summary>
    public string Endpoint { get; set; } = "ydot-minio:9000";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>TLS to the object store. False for the local container, true everywhere real.</summary>
    public bool UseSsl { get; set; }

    /// <summary>Created on startup if missing, with versioning switched on.</summary>
    public string BucketName { get; set; } = "ydot-organisation-documents";

    /// <summary>
    /// The address a BROWSER should use, when it differs from <see cref="Endpoint"/>.
    ///
    /// Inside Docker the API reaches MinIO as "ydot-minio:9000", a name that means nothing on
    /// the user's machine. A download link built from the internal name is signed correctly and
    /// still fails to resolve, which looks like a broken file rather than a networking detail.
    /// Left empty, <see cref="Endpoint"/> is used for both.
    /// </summary>
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>Scheme for <see cref="PublicEndpoint"/>. Independent of <see cref="UseSsl"/>.</summary>
    public bool PublicUseSsl { get; set; }

    /// <summary>
    /// The cap, in megabytes. Enforced on the server; also served to the client so the
    /// upload box can say the number and refuse before spending a person's bandwidth.
    /// </summary>
    public int MaximumFileSizeMegabytes { get; set; } = 5;

    /// <summary>How many files one submission may carry.</summary>
    public int MaximumFilesPerSubmission { get; set; } = 10;

    /// <summary>
    /// How long a download link stays valid.
    ///
    /// A signed link is a bearer credential: whoever holds it can fetch that object until it
    /// expires, with no further permission check. Minutes, therefore — long enough to open a
    /// certificate, short enough that a link pasted into a chat is useless by the time anybody
    /// else clicks it.
    /// </summary>
    public int DownloadLinkExpirySeconds { get; set; } = 300;

    /// <summary>
    /// What may be uploaded, by MIME type.
    ///
    /// AN ALLOWLIST, because the interesting extensions are the ones nobody thought to ban.
    /// The browser's reported type is checked against this AND the extension has to agree with
    /// it, since the type on a form part is caller-supplied like everything else.
    /// </summary>
    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/webp",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    ];

    public long MaximumFileSizeBytes => MaximumFileSizeMegabytes * 1024L * 1024L;
}
