using System.Globalization;
using System.Text;
using YDots.DON.Application.Common.Abstractions.Services;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Builds the controlled CSV exports. Every value is quoted so a comma inside a donor name
/// cannot shift the rest of the row into the wrong columns.
/// </summary>
public sealed class CsvExportService : IExportService
{
    public ExportFile CreateCsv(string fileNamePrefix, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(Quote)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(Quote)));
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var reference = $"EXP-{stamp}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        return new ExportFile(
            $"{fileNamePrefix}-{stamp}.csv",
            "text/csv",
            Encoding.UTF8.GetBytes(builder.ToString()),
            reference);
    }

    private static string Quote(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
