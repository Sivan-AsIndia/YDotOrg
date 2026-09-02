namespace YDots.DON.Application.Features.LeadCapture.DTOs;

/// <summary>
/// One row of an uploaded lead file.
///
/// EVERY FIELD IS A STRING, INCLUDING THE CAMPAIGN. A spreadsheet contains what a person typed,
/// not what the database holds - the campaign arrives as "Clean Water 2026" rather than as a
/// Guid, and a row may name a campaign that does not exist. Resolving and rejecting is the
/// server's job precisely because it is the only side that can tell the difference.
/// </summary>
public sealed record BulkLeadImportRow(
    /// <summary>1-based, as the person sees it in their spreadsheet, so an error names the row they can find.</summary>
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? EmailAddress,
    string? PreferredLanguage,
    string? City,
    string? CampaignNameOrCode,
    string? Source,
    string? Notes);

/// <summary>
/// The uploaded file, parsed into rows.
///
/// THE FILE ITSELF IS NOT SENT. The browser parses the CSV and posts rows, which keeps this
/// endpoint free of file handling and encoding guesswork - and means the person sees the parse
/// result before anything is created.
/// </summary>
public sealed record BulkLeadImportRequest(
    IReadOnlyList<BulkLeadImportRow> Rows,

    /// <summary>Used when a row leaves the campaign blank. Optional.</summary>
    Guid? DefaultCampaignId,

    /// <summary>What to record as the source when a row does not say. Defaults to "Bulk Upload".</summary>
    string? DefaultSource);

/// <summary>What happened to one row.</summary>
public sealed record BulkLeadImportRowResult(
    int RowNumber,
    bool Imported,

    /// <summary>The reference the lead was given, when it was created.</summary>
    string? LeadReference,

    /// <summary>Why it was not, in words the person can act on. Empty when it was.</summary>
    string? Reason);

/// <summary>
/// The outcome of the whole file.
///
/// PARTIAL SUCCESS IS THE NORMAL CASE and is reported as such. A file of two hundred leads with
/// three bad rows should create a hundred and ninety-seven leads and name the three - refusing
/// the file outright would make the person edit a spreadsheet by trial and error.
/// </summary>
public sealed record BulkLeadImportResponse(
    int SubmittedCount,
    int ImportedCount,
    int RejectedCount,
    IReadOnlyList<BulkLeadImportRowResult> Results,
    string Message);
