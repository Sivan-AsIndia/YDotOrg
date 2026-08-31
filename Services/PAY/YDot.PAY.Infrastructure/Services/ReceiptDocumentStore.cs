using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Settings;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Where a rendered receipt document is kept.
///
/// A SEPARATE INTERFACE FROM THE RENDERER because the two change for different reasons: how a
/// receipt LOOKS is a business decision, and where the file LIVES is a deployment one. An
/// installation moving from a mounted volume to S3 or Blob Storage replaces this and nothing
/// else.
///
/// A RECEIPT DOCUMENT HAS TO SURVIVE FOR YEARS - seven or more in most tax jurisdictions - so
/// whatever implements this must be durable storage, not a cache and not a container's own
/// filesystem.
/// </summary>
public interface IReceiptDocumentStore
{
    /// <summary>Stores the document and returns the URL it can be fetched from.</summary>
    Task<string> SaveAsync(
        Guid receiptId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);
}

/// <summary>
/// Stores receipt documents on a filesystem path.
///
/// THE PATH IS EXPECTED TO BE A MOUNTED VOLUME, not a directory inside the container. A container
/// filesystem is discarded on the next deployment, and discarding a donor's tax documents is not
/// a recoverable mistake - which is why the configured root is a deployment decision and the
/// default is a path a compose file mounts.
///
/// THE FILE IS WRITTEN UNDER A DIRECTORY NAMED BY THE RECEIPT ID, which does two things: it stops
/// one organisation's receipt overwriting another's if the numbers ever collide, and it means a
/// correction and the receipt it replaced sit in different directories rather than one clobbering
/// the other.
/// </summary>
public sealed class FileSystemReceiptDocumentStore(
    IOptions<PaymentSettings> paymentSettings,
    IOptions<ClientAppSettings> clientSettings,
    ILogger<FileSystemReceiptDocumentStore> logger) : IReceiptDocumentStore
{
    private readonly PaymentSettings _paymentSettings = paymentSettings.Value;
    private readonly ClientAppSettings _clientSettings = clientSettings.Value;

    public async Task<string> SaveAsync(
        Guid receiptId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var root = string.IsNullOrWhiteSpace(_paymentSettings.ReceiptDocumentRoot)
            ? Path.Combine(AppContext.BaseDirectory, "receipts")
            : _paymentSettings.ReceiptDocumentRoot;

        var directory = Path.Combine(root, receiptId.ToString("N"));

        Directory.CreateDirectory(directory);

        // The caller builds the name, but it reaches here having passed through a receipt number
        // that came from the database - so it is sanitised anyway rather than trusted. A file
        // name containing a path separator would otherwise write outside the directory.
        var safeName = Path.GetFileName(fileName);

        var path = Path.Combine(directory, safeName);

        await File.WriteAllBytesAsync(path, content, cancellationToken);

        logger.LogInformation("Stored the document for receipt {ReceiptId}.", receiptId);

        // The URL the client fetches, which is served by the API's receipt download endpoint
        // rather than by exposing the storage path. A donor's tax document must not be reachable
        // by anybody who can guess a filename.
        var baseUrl = _clientSettings.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/api/v1/receipts/{receiptId}/document";
    }
}
