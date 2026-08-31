namespace YDot.IAM.Domain.Enums;

/// <summary>
/// State of one user-to-role mapping. Assignments are kept rather than deleted when they
/// end, so an access review can still see what somebody used to hold.
/// </summary>
public enum UserRoleAssignmentStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Revoked = 3,
    Expired = 4
}
