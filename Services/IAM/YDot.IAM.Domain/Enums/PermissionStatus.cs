namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.3: Active|Retired. Retired codes stay in the table so old audit rows resolve.</summary>
public enum PermissionStatus
{
    Active = 0,
    Retired = 1
}
