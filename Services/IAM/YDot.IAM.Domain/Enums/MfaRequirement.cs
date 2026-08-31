namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.1: Inherited|Required|Optional. Inherited defers to the Tenant policy.</summary>
public enum MfaRequirement
{
    Inherited = 0,
    Required = 1,
    Optional = 2
}
