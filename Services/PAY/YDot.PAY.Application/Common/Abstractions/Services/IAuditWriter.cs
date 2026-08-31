using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// Writes the append-only payment audit trail.
///
/// THE ROW IS ASSEMBLED BY THE IMPLEMENTATION. Actor, Organisation, IP address, correlation id
/// and timestamp all come from the ambient request context, so a handler supplies only what is
/// specific to the action - which is what keeps the trail complete.
///
/// THE METADATA IS SCRUBBED ON THE WAY IN. Card numbers, CVVs and gateway secrets never reach
/// this table: an audit trail that leaks the thing it was auditing is a liability rather than a
/// control.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        AuditResult result,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An action with no authenticated caller: a public donation, a gateway webhook.
    ///
    /// The Organisation is passed explicitly because there is no token to read it from - and
    /// these are precisely the rows an investigation looks at first, so they must not be
    /// recorded as belonging to nobody.
    /// </summary>
    Task WriteAnonymousAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        Guid? tenantId,
        AuditResult result,
        object? metadata = null,
        CancellationToken cancellationToken = default);
}
