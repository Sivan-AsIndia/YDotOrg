using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// <see cref="IObjectStorage"/> on MinIO.
///
/// THE ONLY FILE IN THE SOLUTION THAT KNOWS MINIO EXISTS. Everything else works through the
/// interface, so replacing this with S3 or Azure Blob is one new class and one line of
/// registration.
///
/// TWO CLIENTS, AND THE SECOND ONE IS NOT AN ACCIDENT. Inside Docker the API reaches MinIO at
/// "ydot-minio:9000", a name that resolves on the container network and nowhere else. A
/// download URL signed against that name is perfectly valid and completely unreachable from
/// the user's browser. The public client exists purely to sign links against the address a
/// browser can actually resolve. A signature covers the host, so the same client cannot do
/// both jobs.
/// </summary>
public sealed class MinioObjectStorage : IObjectStorage, IDisposable
{
    private readonly DocumentStorageSettings _settings;
    private readonly ILogger<MinioObjectStorage> _logger;
    private readonly IMinioClient _internalClient;
    private readonly IMinioClient _publicClient;
    private readonly bool _publicClientIsSeparate;

    public MinioObjectStorage(
        IOptions<DocumentStorageSettings> options,
        ILogger<MinioObjectStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _settings = options.Value;
        _logger = logger;

        _internalClient = Build(_settings.Endpoint, _settings.UseSsl);

        _publicClientIsSeparate = !string.IsNullOrWhiteSpace(_settings.PublicEndpoint)
                                  && !string.Equals(
                                      _settings.PublicEndpoint, _settings.Endpoint, StringComparison.OrdinalIgnoreCase);

        _publicClient = _publicClientIsSeparate
            ? Build(_settings.PublicEndpoint, _settings.PublicUseSsl)
            : _internalClient;
    }

    private IMinioClient Build(string endpoint, bool useSsl) =>
        new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(_settings.AccessKey, _settings.SecretKey)
            .WithSSL(useSsl)
            .Build();

    /// <summary>
    /// Creates the bucket and turns versioning on.
    ///
    /// VERSIONING IS THE WHOLE RE-UPLOAD STORY. When a rejected certificate is replaced, the
    /// bytes that were actually reviewed must not disappear — an audit that can only show the
    /// current file cannot answer "what did the reviewer see when they said no?". With
    /// versioning on, writing the same key keeps the old object and returns a new version id,
    /// which the document row stores.
    ///
    /// Failure here is logged and swallowed on purpose. A storage outage must not stop IAM from
    /// starting: sign-in, roles and the whole of the rest of the service have nothing to do
    /// with documents, and taking authentication down over a file store would turn a small
    /// outage into a total one. Uploads fail with a clear message until it is back.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await _internalClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_settings.BucketName), cancellationToken);

            if (!exists)
            {
                await _internalClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(_settings.BucketName), cancellationToken);

                _logger.LogInformation("Created object storage bucket {Bucket}.", _settings.BucketName);
            }

            await _internalClient.SetVersioningAsync(
                new SetVersioningArgs().WithBucket(_settings.BucketName).WithVersioningEnabled(),
                cancellationToken);

            _logger.LogInformation(
                "Object storage is ready: bucket {Bucket} at {Endpoint}, versioning enabled.",
                _settings.BucketName, _settings.Endpoint);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Object storage at {Endpoint} could not be prepared. Document upload will fail "
                + "until it is reachable; the rest of IAM is unaffected.",
                _settings.Endpoint);
        }
    }

    /// <summary>
    /// Streams the file in and hashes it in the same pass.
    ///
    /// The stream is copied to a temporary file first, for one reason: the hash has to be
    /// computed over the whole content, and MinIO needs to send that same content afterwards.
    /// Reading a forward-only upload stream twice is not possible, and buffering a 5 MB file in
    /// memory per concurrent upload is how a service falls over under load.
    /// </summary>
    public async Task<StoredObject> PutAsync(
        string storagePath,
        Stream content,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var scratchPath = Path.Combine(Path.GetTempPath(), $"ydot-upload-{Guid.NewGuid():N}");

        try
        {
            string hash;
            long actualSize;

            await using (var scratch = new FileStream(
                scratchPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(scratch, cancellationToken);
                actualSize = scratch.Length;

                scratch.Position = 0;
                var digest = await SHA256.HashDataAsync(scratch, cancellationToken);
                hash = Convert.ToHexStringLower(digest);

                scratch.Position = 0;

                var put = new PutObjectArgs()
                    .WithBucket(_settings.BucketName)
                    .WithObject(storagePath)
                    .WithStreamData(scratch)
                    .WithObjectSize(actualSize)
                    .WithContentType(contentType);

                await _internalClient.PutObjectAsync(put, cancellationToken);

                // The version id is not on the put response in this client, so it is read back
                // with a stat. One extra round trip per upload, and worth it: without the
                // version the audit trail can only ever re-open whatever currently sits at this
                // key, which after a re-upload is precisely the wrong file.
                var versionId = await ReadVersionIdAsync(storagePath, cancellationToken);

                return new StoredObject(storagePath, versionId, actualSize, hash);
            }
        }
        finally
        {
            // Best effort. A leftover scratch file is untidy; throwing from a finally block
            // while an upload is already failing would replace the real error with this one.
            try
            {
                if (File.Exists(scratchPath))
                {
                    File.Delete(scratchPath);
                }
            }
            catch (IOException)
            {
                // Ignored deliberately.
            }
        }
    }

    /// <summary>
    /// A presigned URL, signed against the browser-facing address.
    ///
    /// <paramref name="inline"/> decides between previewing and saving, through the
    /// Content-Disposition the store will return. Inline is what lets a PDF or an image render
    /// inside the review screen instead of landing in the downloads folder — and forcing the
    /// original file name onto the response is what stops a reviewer receiving a file called
    /// "0f3c9d1e-…" with no extension, which is the metadata error that made these unopenable.
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(
        string storagePath,
        string? versionId,
        string downloadFileName,
        bool inline,
        CancellationToken cancellationToken)
    {
        var disposition = inline ? "inline" : "attachment";
        var safeName = SanitiseHeaderFileName(downloadFileName);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response-content-disposition"] = $"{disposition}; filename=\"{safeName}\""
        };

        // These are QUERY PARAMETERS on a presigned URL, not HTTP headers, whatever the method
        // is called. That is what makes them work: they are covered by the signature and travel
        // in the link, so a browser following it needs to send nothing of its own. versionId
        // rides in the same dictionary for the same reason.
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            headers["versionId"] = versionId;
        }

        var args = new PresignedGetObjectArgs()
            .WithBucket(_settings.BucketName)
            .WithObject(storagePath)
            .WithExpiry(_settings.DownloadLinkExpirySeconds)
            .WithHeaders(headers);

        return await _publicClient.PresignedGetObjectAsync(args);
    }

    public async Task RemoveAsync(string storagePath, string? versionId, CancellationToken cancellationToken)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(_settings.BucketName)
            .WithObject(storagePath);

        if (!string.IsNullOrWhiteSpace(versionId))
        {
            args = args.WithVersionId(versionId);
        }

        await _internalClient.RemoveObjectAsync(args, cancellationToken);
    }

    /// <summary>
    /// Reads back the version the store assigned to the object just written.
    ///
    /// A failure here is not fatal: the bytes are already stored and the document is usable.
    /// Losing the version id costs the ability to re-open this exact revision later, which is
    /// worth a warning rather than throwing away a successful upload.
    /// </summary>
    private async Task<string?> ReadVersionIdAsync(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await _internalClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(_settings.BucketName).WithObject(storagePath),
                cancellationToken);

            return string.IsNullOrWhiteSpace(stat.VersionId) ? null : stat.VersionId;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Stored {StoragePath} but could not read its version id. The file is usable; "
                + "this revision cannot be re-opened specifically later.",
                storagePath);

            return null;
        }
    }

    /// <summary>
    /// Makes a file name safe to put inside a Content-Disposition header.
    ///
    /// A quote or a newline in that header is header injection, not a filing problem. Anything
    /// outside a conservative set is replaced rather than escaped, because the name only has to
    /// be recognisable to a person — the real identity of the object is its key.
    /// </summary>
    private static string SanitiseHeaderFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "document";
        }

        var cleaned = new string([.. trimmed.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ' '
                ? character
                : '_')]);

        return cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }

    public void Dispose()
    {
        _internalClient.Dispose();

        if (_publicClientIsSeparate)
        {
            _publicClient.Dispose();
        }
    }
}
