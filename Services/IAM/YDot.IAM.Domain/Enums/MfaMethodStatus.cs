namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.6: Pending|Active|Revoked. Pending means enrolled but not yet proven.</summary>
public enum MfaMethodStatus
{
    Pending = 0,
    Active = 1,
    Revoked = 2
}
