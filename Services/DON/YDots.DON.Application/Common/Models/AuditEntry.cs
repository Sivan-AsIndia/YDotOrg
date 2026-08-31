using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Common.Models;

/// <summary>
/// The redacted audit record written by <c>IAuditWriter</c>, exactly as the application
/// interface table in section 6 requires.
/// </summary>
public sealed record AuditEntry(
    string ActionCode,
    string TargetType,
    Guid? TargetId,
    AuditResult Result = AuditResult.Succeeded,
    string? Reason = null,
    Guid? OrganisationId = null);
