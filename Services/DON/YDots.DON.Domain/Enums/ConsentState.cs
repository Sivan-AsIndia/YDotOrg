namespace YDots.DON.Domain.Enums;

/// <summary>
/// The decision recorded on a consent row. Different from <see cref="ConsentStatus"/>:
/// the state is what the donor said, the status is where the row sits in its own lifecycle.
/// </summary>
public enum ConsentState
{
    NotProvided = 0,
    Granted = 1,
    Withdrawn = 2,
    Pending = 3
}
