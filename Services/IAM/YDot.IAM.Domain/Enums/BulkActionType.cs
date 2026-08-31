namespace YDot.IAM.Domain.Enums;

/// <summary>The actions IAM-USR-06 Bulk user administration can apply to a selection.</summary>
public enum BulkActionType
{
    Invite = 0,
    Activate = 1,
    Suspend = 2,
    Reactivate = 3,
    Deactivate = 4,
    AssignRole = 5,
    RemoveRole = 6,
    ResetPassword = 7,
    ForceSignOut = 8,
    RequireMfaReset = 9,
    ExtendAccess = 10,
    Export = 11
}
