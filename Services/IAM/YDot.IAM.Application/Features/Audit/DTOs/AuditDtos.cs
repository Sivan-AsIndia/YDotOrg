using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Audit.DTOs;

/// <summary>
/// One audit row as the trail screen shows it.
///
/// <c>Metadata</c> is already redacted by the time it reaches here - the audit writer scrubs
/// anything resembling a password, token or secret before it is ever serialised. An audit
/// trail that leaks the thing it was auditing is a liability rather than a control.
///
/// <c>ActorScope</c> plus <c>TenantName</c> is what lets an Organisation see "somebody from
/// the platform did this to my data" without the row pretending the actor was one of their
/// own users.
/// </summary>
public sealed record AuditEventResponse(
    Guid Id,
    Guid? TenantId,
    string? TenantName,
    Guid BusinessUnitId,
    Guid? ActorUserId,
    string? ActorDisplayName,
    AccessScopeType ActorScope,
    string ActionCode,
    string ActionDisplay,
    string TargetType,
    Guid? TargetId,
    string? TargetDisplayName,
    AuditResult Result,
    string ResultDisplay,
    string? Reason,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string? IpAddress,
    string? UserAgent,
    ClientType ClientType,
    Guid? SessionId,
    bool IsSensitive,
    string? RequestPath,

    /// <summary>Redacted detail. Null when the caller lacks the view-sensitive permission.</summary>
    string? Metadata);

/// <summary>
/// One row of a CSV export of the trail.
///
/// <c>SupportReference</c> is the correlation id's first eight characters, upper-case - the same
/// short reference the screen shows. The full id is a GUID, which nobody reading a spreadsheet can
/// use; the log is searched by prefix.
/// </summary>
public sealed record AuditExportRow(
    string OccurredAtUtc,
    string? Organisation,
    string? Actor,
    string ActorScope,
    string ActionCode,
    string TargetType,
    string? Target,
    string Result,
    string? Reason,
    string? IpAddress,
    string ClientType,
    string SupportReference)
{
    /// <summary>The short, quotable form of a correlation id: its first eight characters, upper-case.</summary>
    public static string ShortReference(string? correlationId)
    {
        var compact = new string((correlationId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

        return compact.Length == 0 ? string.Empty : compact[..Math.Min(8, compact.Length)].ToUpperInvariant();
    }
}
