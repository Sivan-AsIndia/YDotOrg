namespace YDots.DON.Domain.Enums;

/// <summary>DON-UI-07 lifecycle for one identity verification attempt.</summary>
public enum VerificationStatus
{
    NotStarted = 0,
    ChallengeSent = 1,
    Verified = 2,
    Failed = 3,
    Escalated = 4,
    Cancelled = 5,
    Expired = 6
}
