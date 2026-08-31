using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>
/// Appends one redacted audit record, exactly as the section 6 interface table requires.
///
/// The row is staged on the same unit of work as the business change, so the action and its
/// audit trail commit together or not at all. Section 10 redaction rule: no credentials, no
/// tokens, no payment instrument data, no document bytes and no unnecessary personal values.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
