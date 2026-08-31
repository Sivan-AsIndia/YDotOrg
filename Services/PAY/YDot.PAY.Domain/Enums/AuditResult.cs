namespace YDot.PAY.Domain.Enums;

/// <summary>The outcome recorded on an audit row.</summary>
public enum AuditResult
{
    Succeeded = 0,
    Failed = 1,

    /// <summary>Refused by an authorisation or business rule. The row worth alerting on.</summary>
    Denied = 2
}
