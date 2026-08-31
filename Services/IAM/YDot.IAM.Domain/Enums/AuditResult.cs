namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.8: Succeeded|Denied|Failed. Denied is an authorisation refusal;
/// Failed is a dependency or system error.</summary>
public enum AuditResult
{
    Succeeded = 0,
    Denied = 1,
    Failed = 2
}
