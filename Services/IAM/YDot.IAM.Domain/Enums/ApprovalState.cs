namespace YDot.IAM.Domain.Enums;

/// <summary>Maker/checker state shared by the records that need a second pair of eyes.</summary>
public enum ApprovalState
{
    NotSubmitted = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3
}
