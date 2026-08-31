namespace YDot.IAM.Domain.Enums;

/// <summary>What is being asked for. Temporary access must carry an end date.</summary>
public enum AccessRequestType
{
    RoleAssignment = 0,
    PermissionGrant = 1,
    DataScopeGrant = 2,
    TemporaryElevation = 3
}
