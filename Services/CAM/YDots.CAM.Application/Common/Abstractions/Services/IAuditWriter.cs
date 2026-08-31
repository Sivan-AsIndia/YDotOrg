using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// Writes the append-only campaign audit trail.
///
/// THE ROW IS ASSEMBLED BY THE IMPLEMENTATION, NOT BY THE CALLER. Actor, Organisation,
/// BusinessUnit, IP address, correlation id and timestamp all come from the ambient request
/// context, so a handler supplies only what is specific to the action. That is what keeps the
/// trail complete: there is no field a busy handler can forget.
///
/// IT USED TO TAKE A FULLY-BUILT <c>CampaignAuditEvent</c>, which meant every call site decided
/// for itself which of those ambient fields to populate - and the ones that forgot produced
/// rows with no actor and no correlation id, indistinguishable from a system action.
/// </summary>
public interface IAuditWriter
{
    /// <summary>A successful action.</summary>
    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>An action that was refused or failed, with the outcome recorded.</summary>
    Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        AuditResult result,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
