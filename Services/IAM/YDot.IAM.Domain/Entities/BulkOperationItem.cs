using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One row of a bulk job, with its own outcome.
///
/// Per-row rather than a single job-level result, because the operator needs to know which
/// three of the four hundred failed and why, in a form they can correct and re-upload.
/// </summary>
public class BulkOperationItem : TenantEntity
{
    public Guid BulkOperationId { get; set; }

    public BulkOperation? BulkOperation { get; set; }

    /// <summary>Position in the source file, so a message can name the line.</summary>
    public int RowNumber { get; set; }

    /// <summary>Null when the row did not match an existing user.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The identifier as it appeared in the file.</summary>
    public string? SourceIdentifier { get; set; }

    /// <summary>The raw row, kept so the result file can echo the input back.</summary>
    public string? SourceData { get; set; }

    public bool IsValid { get; set; }

    /// <summary>Why validation refused this row.</summary>
    public string? ValidationMessage { get; set; }

    public bool IsProcessed { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>True when the row was deliberately passed over: already in the target state.</summary>
    public bool WasSkipped { get; set; }

    public string? ResultMessage { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
