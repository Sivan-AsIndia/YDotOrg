namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Lifecycle of a bulk job. <see cref="PartiallySucceeded"/> exists because a 400-row job
/// where 397 worked is neither a success nor a failure, and telling the operator "failed"
/// would send them to undo work that actually landed.
/// </summary>
public enum BulkOperationStatus
{
    Draft = 0,
    Validating = 1,
    Validated = 2,
    Queued = 3,
    Running = 4,
    Completed = 5,
    PartiallySucceeded = 6,
    Failed = 7,
    Cancelled = 8
}
