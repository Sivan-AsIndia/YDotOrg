namespace YDots.DON.Domain.Enums;

/// <summary>"Last contact outcome" badge on the lead work queue.</summary>
public enum ContactOutcome
{
    NotContacted = 0,
    Reached = 1,
    NoAnswer = 2,
    CallbackRequested = 3,
    NotInterested = 4,
    WrongNumber = 5,
    DoNotContact = 6
}
