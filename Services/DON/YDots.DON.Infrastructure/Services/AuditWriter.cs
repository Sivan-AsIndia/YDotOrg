using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Entities;
using YDots.DON.Infrastructure.Persistence;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Adds one audit row to the current unit of work. The row is committed together with the
/// business change, so an action and its audit trail can never drift apart.
///
/// Nothing is written to the database here — only staged. The handler's single
/// SaveChangesAsync is what makes the pair atomic.
/// </summary>
public sealed class AuditWriter(
    DonDbContext context,
    ICurrentUser currentUser,
    IDonorMetrics metrics) : IAuditWriter
{
    private const int ReasonMaximumLength = 2000;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        // Every state change writes an audit row by contract, so this is also the one place
        // that sees every lifecycle transition — which makes it the natural place to count them.
        metrics.RecordTransition(entry.ActionCode, entry.TargetType, entry.Result.ToString());

        context.AuditEvents.Add(new DonorAuditEvent
        {
            OrganisationId = entry.OrganisationId ?? currentUser.OrganisationId,
            ActorUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
            ActionCode = entry.ActionCode,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Result = entry.Result,
            Reason = Truncate(entry.Reason),
            CorrelationId = currentUser.CorrelationId,
            IpAddress = currentUser.IpAddress
        });

        return Task.CompletedTask;
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= ReasonMaximumLength ? value
        : value[..ReasonMaximumLength];
}
