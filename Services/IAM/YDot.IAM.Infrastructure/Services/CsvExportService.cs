using System.Globalization;
using System.Reflection;
using System.Text;
using YDot.IAM.Application.Common.Abstractions.Services;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Turns a projection into a CSV download.
///
/// TWO THINGS HERE ARE LESS OBVIOUS THAN THEY LOOK.
///
/// FIRST, THE BYTE ORDER MARK. Excel on Windows opens a UTF-8 file WITHOUT a BOM using the
/// system code page, so any name with an accent in it arrives mangled. Emitting the BOM is
/// what makes an export of Indian or European names readable to the person who asked for it.
///
/// SECOND, THE FORMULA GUARD. A cell beginning with = + - or @ is executed as a formula when
/// the file is opened. A user record whose display name is
/// <c>=HYPERLINK("http://evil","click")</c> therefore becomes an attack on whoever opens the
/// spreadsheet — CSV injection. Prefixing a tab neutralises it while leaving the value
/// readable, which is why every field goes through <see cref="Escape"/> rather than only the
/// ones that look risky.
/// </summary>
public sealed class CsvExportService : IExportService
{
    public ExportFile ToCsv<T>(IEnumerable<T> rows, string fileNameWithoutExtension, string reference)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .ToArray();

        var builder = new StringBuilder();

        builder.AppendLine(string.Join(',', properties.Select(property => Escape(Humanise(property.Name)))));

        foreach (var row in rows)
        {
            var values = properties.Select(property => Escape(Format(property.GetValue(row))));
            builder.AppendLine(string.Join(',', values));
        }

        // UTF-8 WITH the BOM. See the note above.
        //
        // THE PREAMBLE IS PREPENDED BY HAND, and it has to be: constructing a UTF8Encoding with
        // encoderShouldEmitUTF8Identifier does NOT make GetBytes emit the mark. That flag only
        // affects writers that ask the encoding for its preamble — a StreamWriter does,
        // GetBytes never does. Relying on the flag alone produces a file that looks correct in
        // every editor and mangles every accented name the moment Excel opens it.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(builder.ToString());

        var content = new byte[preamble.Length + body.Length];
        preamble.CopyTo(content, 0);
        body.CopyTo(content, preamble.Length);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{fileNameWithoutExtension}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");

        return new ExportFile(content, "text/csv", fileName, reference);
    }

    /// <summary>
    /// Quotes a value for CSV, and defuses anything a spreadsheet would treat as a formula.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value;

        // CSV injection. A leading =, +, - or @ makes the cell a formula when opened.
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "\t" + text;
        }

        // Standard CSV quoting: wrap when the value contains a delimiter, a quote or a
        // newline, and double any embedded quote.
        if (text.Contains('"', StringComparison.Ordinal)
            || text.Contains(',', StringComparison.Ordinal)
            || text.Contains('\n', StringComparison.Ordinal)
            || text.Contains('\r', StringComparison.Ordinal))
        {
            text = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }

    /// <summary>
    /// Renders a value for a spreadsheet.
    ///
    /// Dates go out as ISO 8601 rather than a local format, because a CSV has no way to say
    /// which locale it was written in and "03/04" is genuinely ambiguous across the world.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool flag => flag ? "Yes" : "No",
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        Enum enumeration => enumeration.ToString(),
        System.Collections.IEnumerable sequence and not string =>
            string.Join("; ", sequence.Cast<object?>().Select(item => item?.ToString())),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>"DisplayName" becomes "Display Name", so the header row reads as English.</summary>
    private static string Humanise(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var builder = new StringBuilder(propertyName.Length + 8);
        builder.Append(propertyName[0]);

        for (var index = 1; index < propertyName.Length; index++)
        {
            if (char.IsUpper(propertyName[index]) && !char.IsUpper(propertyName[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(propertyName[index]);
        }

        return builder.ToString();
    }
}
