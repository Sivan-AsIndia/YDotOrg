using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// Writes the append-only audit trail.
///
/// THE ROW IS ASSEMBLED HERE, NOT BY THE CALLER. Actor, Organisation, scope, IP, user agent,
/// client type, session and correlation id all come from the ambient request context, so a
/// handler supplies only what is specific to the action. That is what keeps the trail
/// complete: there is no field a busy handler can forget.
///
/// EVERYTHING IS REDACTED ON THE WAY IN. The metadata passed here goes through a scrubber
/// that drops anything resembling a password, token, secret or hash before it is serialised.
/// An audit trail that leaks the thing it was auditing is a liability rather than a control.
/// </summary>
public interface IAuditService
{
    /// <summary>A successful action.</summary>
    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        string? targetDisplayName = null,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>An action that was refused, with the outcome recorded.</summary>
    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        AuditResult result,
        string? targetDisplayName = null,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An action taken when there is no authenticated caller — a failed sign-in, an
    /// invitation accepted by somebody who is not yet a user. The actor and the Organisation
    /// are passed explicitly because there is no context to read them from.
    /// </summary>
    Task WriteAnonymousAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        Guid businessUnitId,
        Guid? tenantId,
        AuditResult result,
        string? targetDisplayName = null,
        object? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A cross-Tenant access attempt.
    ///
    /// The query filters make this all but unreachable, so a row written here means somebody
    /// got past several layers on purpose. It is logged at warning level as well as recorded,
    /// because it is the one audit event that should wake somebody up.
    /// </summary>
    Task WriteCrossTenantAttemptAsync(
        string targetType,
        Guid? targetId,
        Guid attemptedTenantId,
        CancellationToken cancellationToken = default);
}
