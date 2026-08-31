namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.5: Open|InProgress|Completed|Overdue|Cancelled.</summary>
public enum AccessReviewStatus
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
    Overdue = 3,
    Cancelled = 4
}
