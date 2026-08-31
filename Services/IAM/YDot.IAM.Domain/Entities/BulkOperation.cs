using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// IAM-USR-06: one bulk job over many users.
///
/// VALIDATE, THEN APPLY. The job goes through Validating and Validated before anything is
/// written, so an operator sees "12 of these 400 rows will fail, here is why" while it is
/// still cheap to fix. Applying a 400-row change and reporting the failures afterwards is a
/// far worse experience and often not reversible.
///
/// PARTIAL SUCCESS IS A REAL OUTCOME. If 397 rows succeed and 3 fail, the job is
/// PartiallySucceeded rather than Failed — reporting failure would send somebody to undo
/// work that actually landed.
/// </summary>
public class BulkOperation : TenantEntity
{
    /// <summary>Unique inside the Tenant, for example BLK-2026-00017.</summary>
    public string OperationNumber { get; set; } = string.Empty;

    public BulkActionType ActionType { get; set; }

    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Draft;

    /// <summary>The uploaded file, when the selection came from a spreadsheet.</summary>
    public string? SourceFileName { get; set; }

    public string? SourceStoragePath { get; set; }

    /// <summary>Serialised parameters: which role to add, how long to extend access.</summary>
    public string? ActionParameters { get; set; }

    public int TotalItemCount { get; set; }

    public int ProcessedItemCount { get; set; }

    public int SucceededItemCount { get; set; }

    public int FailedItemCount { get; set; }

    public int SkippedItemCount { get; set; }

    public DateTimeOffset? ValidatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid RequestedByUserId { get; set; }

    /// <summary>Set when the action is sensitive enough to need a second pair of eyes.</summary>
    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>Summary message for the operator when the job ends badly.</summary>
    public string? FailureSummary { get; set; }

    /// <summary>Generated result file listing the per-row outcome.</summary>
    public string? ResultStoragePath { get; set; }

    public string? CorrelationId { get; set; }

    public ICollection<BulkOperationItem> Items { get; set; } = [];

    public bool IsTerminal => Status is BulkOperationStatus.Completed or BulkOperationStatus.Failed
        or BulkOperationStatus.PartiallySucceeded or BulkOperationStatus.Cancelled;

    public int PercentComplete => TotalItemCount == 0
        ? 0
        : (int)Math.Round(ProcessedItemCount * 100.0 / TotalItemCount);
}
