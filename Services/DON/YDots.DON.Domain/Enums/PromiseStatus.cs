namespace YDots.DON.Domain.Enums;

/// <summary>Lifecycle of a pledge the donor made. Feeds the Promises panel on Donor 360.</summary>
public enum PromiseStatus
{
    Open = 1,
    PartiallyFulfilled = 2,
    Fulfilled = 3,
    Lapsed = 4,
    Cancelled = 5
}
