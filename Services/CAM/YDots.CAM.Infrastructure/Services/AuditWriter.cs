using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;
using YDots.CAM.Infrastructure.Persistence;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// Writes the append-only campaign audit trail.
///
/// THE ROW IS ASSEMBLED HERE, NOT BY THE CALLER. Actor, Organisation, BusinessUnit, IP address,
/// correlation id and timestamp all come from the ambient request context, so a handler
/// supplies only what is specific to the action. That is what keeps the trail complete: there
/// is no field a busy handler can forget.
///
/// IT ADDS TO THE CHANGE TRACKER AND DOES NOT SAVE. The audit row commits in the same
/// transaction as the change it records, which is the only way the two can never disagree - an
/// audit row saved separately can survive a rolled-back change, and a change can commit without
/// its audit row.
/// </summary>
public sealed class AuditWriter(
    CampaignDbContext context,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IAuditWriter
{
    private const int ReasonMaximumLength = 2000;

    public Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(actionCode, targetType, targetId, AuditResult.Succeeded, reason, cancellationToken);

    public async Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        AuditResult result,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(
            new CampaignAuditEvent
            {
                TenantId = tenantContext.TenantId,
                BusinessUnitId = tenantContext.BusinessUnitId == Guid.Empty
                    ? null
                    : tenantContext.BusinessUnitId,

                // Null rather than Guid.Empty for an unauthenticated actor, so "nobody was
                // signed in" is distinguishable from "the actor was not recorded".
                ActorUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,

                ActionCode = actionCode,
                TargetType = targetType,
                TargetId = targetId,
                Result = result,
                Reason = Truncate(reason),
                CorrelationId = currentUser.CorrelationId,
                IpAddress = currentUser.IpAddress,
                OccurredAtUtc = clock.UtcNow
            },
            cancellationToken);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= ReasonMaximumLength ? value : value[..ReasonMaximumLength];
    }
}
