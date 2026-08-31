namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// What each accepted content type is called, what it is named on disk, and whether a browser
/// can show it without downloading it first.
///
/// WHY THE EXTENSION IS CHECKED AGAINST THE TYPE. A browser reports a file's content type by
/// looking at its extension, so the two always agree when a person picks a file from the
/// picker — and a caller posting the form by hand can make them say whatever they like.
/// Requiring them to line up costs nothing in the honest case and refuses "payload.exe"
/// announced as "application/pdf".
///
/// This is not a substitute for scanning content. It is the cheap check that removes the
/// obvious cases before anything is stored.
/// </summary>
public static class DocumentContentTypes
{
    private static readonly Dictionary<string, string[]> ExtensionsByType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["image/png"] = [".png"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/webp"] = [".webp"],
            ["application/msword"] = [".doc"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
            ["application/vnd.ms-excel"] = [".xls"],
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = [".xlsx"]
        };

    /// <summary>
    /// The types a browser renders in place.
    ///
    /// Only these get an inline preview in the review screen. A Word document handed to a
    /// browser inline is downloaded anyway on most setups, so offering "preview" for one would
    /// promise something that does not happen.
    /// </summary>
    private static readonly HashSet<string> Previewable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf", "image/png", "image/jpeg", "image/webp"
        };

    /// <summary>Does this file name's extension match what the type claims?</summary>
    public static bool ExtensionMatches(string contentType, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return ExtensionsByType.TryGetValue(contentType, out var allowed)
               && allowed.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Can the review screen show this in place, rather than only offering to save it?</summary>
    public static bool IsPreviewable(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && Previewable.Contains(contentType);

    /// <summary>Every extension behind a set of accepted types, for the file picker's filter.</summary>
    public static IReadOnlyList<string> ExtensionsFor(IEnumerable<string> contentTypes) =>
        [.. contentTypes
            .Where(ExtensionsByType.ContainsKey)
            .SelectMany(type => ExtensionsByType[type])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// A short label for the queue — "PDF", "PNG" — so a reviewer can see what is inside a
    /// submission without opening it.
    /// </summary>
    public static string ShortLabel(string? contentType) => contentType switch
    {
        "application/pdf" => "PDF",
        "image/png" => "PNG",
        "image/jpeg" => "JPEG",
        "image/webp" => "WEBP",
        "application/msword" or
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "Word",
        "application/vnd.ms-excel" or
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "Excel",
        _ => "File"
    };
}
