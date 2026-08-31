using System.Globalization;
using System.Reflection;
using System.Text;
using YDot.PAY.Application.Common.Abstractions.Services;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Turns a projection into a CSV.
///
/// IT WRITES A UTF-8 BOM, deliberately. Excel on Windows reads a BOM-less UTF-8 file as the
/// system code page, which turns every accented donor name and every currency symbol into
/// mojibake - and the person who opens the file has no way to tell that the data was fine and
/// the encoding was not.
///
/// EVERY FIELD IS QUOTED AND EVERY QUOTE DOUBLED. Donor names, refund reasons and receipt notes are
/// free text that routinely contains commas, quotes and newlines, and a naive join produces a
/// file whose columns silently shift half way down.
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    public ExportFile ToCsv<T>(IEnumerable<T> rows, string fileNameWithoutExtension)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .ToList();

        var builder = new StringBuilder();

        builder.AppendLine(string.Join(',', properties.Select(property => Escape(property.Name))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', properties.Select(property =>
                Escape(Format(property.GetValue(row))))));
        }

        // The BOM is what makes Excel read this as UTF-8. See the class comment.
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(builder.ToString());

        var reference = $"EXP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var fileName =
            $"{fileNameWithoutExtension}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

        return new ExportFile(content, "text/csv; charset=utf-8", fileName, reference);
    }

    /// <summary>
    /// Renders a value for a CSV cell.
    ///
    /// The invariant culture throughout: a file produced on a server with a comma decimal
    /// separator would put "1,50" in a comma-separated file, which splits the column.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset moment => moment.ToString("u", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("u", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? "Yes" : "No",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Quotes a field and doubles any quote inside it.
    ///
    /// The leading apostrophe guard is the one that is easy to miss: a cell starting =, +, - or
    /// @ is executed as a FORMULA by Excel when the file is opened. A campaign named
    /// "=cmd|..." is a real injection vector into whoever opens the export, and prefixing a
    /// tab neutralises it without changing what the reader sees.
    /// </summary>
    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@'))
        {
            text = "\t" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
