namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>Turns a projection into a downloadable file for the export endpoints.</summary>
public interface IExportService
{
    /// <summary>
    /// CSV from a row set. The reference returned on the file is written into the audit row
    /// and echoed in a response header, so a spreadsheet found on somebody desktop months
    /// later can be traced back to who exported it and when.
    /// </summary>
    ExportFile ToCsv<T>(IEnumerable<T> rows, string fileNameWithoutExtension, string reference);
}

/// <summary>A generated file plus its audit reference.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName, string Reference);
