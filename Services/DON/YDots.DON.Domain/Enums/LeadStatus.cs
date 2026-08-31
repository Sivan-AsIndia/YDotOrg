namespace YDots.DON.Domain.Enums;

/// <summary>
/// The eight lifecycle states from UI section 5.5. Every one of them needs its own
/// presentation and its own set of allowed actions.
/// </summary>
public enum LeadStatus
{
    New = 1,
    Assigned = 2,
    Contacted = 3,
    Qualified = 4,
    Converted = 5,
    Nurture = 6,
    Closed = 7,
    Suppressed = 8
}
