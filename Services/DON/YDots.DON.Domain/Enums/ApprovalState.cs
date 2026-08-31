namespace YDots.DON.Domain.Enums;

/// <summary>
/// Maker / checker state that sits beside <see cref="DonorStatus"/>.
///
/// The status enum in the contract has no "waiting for approval" member, but section 7 of
/// the contract requires a Submit then Approve pair with segregation of duties. The record
/// therefore keeps its business status and records the approval position separately.
/// </summary>
public enum ApprovalState
{
    NotSubmitted = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5
}
