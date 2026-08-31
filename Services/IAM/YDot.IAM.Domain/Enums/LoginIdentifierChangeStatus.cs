namespace YDot.IAM.Domain.Enums;

/// <summary>
/// IAM-USR-05. Changing the address you sign in with is a two-sided proof: the new address
/// has to be confirmed, and on a sensitive account the old one is notified so a silent
/// takeover is impossible.
/// </summary>
public enum LoginIdentifierChangeStatus
{
    Draft = 0,
    PendingVerification = 1,
    PendingApproval = 2,
    Approved = 3,
    Applied = 4,
    Rejected = 5,
    Cancelled = 6,
    Expired = 7
}
