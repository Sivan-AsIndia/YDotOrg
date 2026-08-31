namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>Turns a projection into a downloadable file for the export endpoints.</summary>
public interface ICsvExportService
{
    /// <summary>
    /// CSV from a row set.
    ///
    /// The reference on the returned file is written into the audit row and echoed in a response
    /// header, so a spreadsheet of donation records found months later can be traced back to who
    /// exported it.
    /// </summary>
    ExportFile ToCsv<T>(IEnumerable<T> rows, string fileNameWithoutExtension);
}

/// <summary>A generated file plus its audit reference.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName, string Reference);
