namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>Result of a controlled export: the file bytes plus a stable reference for the audit trail.</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content, string Reference);

/// <summary>
/// Builds the controlled CSV exports offered by the donor list and the consent history.
/// UI section 6.2: purpose, scope, classification, expiry, row count and audit reference are
/// all visible, and a classified export is a request flow rather than an instant download.
/// </summary>
public interface IExportService
{
    ExportFile CreateCsv(string fileNamePrefix, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
}
