namespace YDots.DON.Domain.Enums;

/// <summary>
/// SLA badge on the lead work queue and the assignment board. Derived from the
/// next-action due date, never entered by hand.
/// </summary>
public enum SlaState
{
    NotApplicable = 0,
    OnTrack = 1,
    DueToday = 2,
    Overdue = 3,
    Breached = 4
}
