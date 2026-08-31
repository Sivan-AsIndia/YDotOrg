using System.Text.Json;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Domain.Entities;
using YDots.DON.Infrastructure.Persistence;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Stages one integration event on the current unit of work.
///
/// The row lands in the same SaveChanges as the business change, which is the entire point of
/// an outbox: another section can never be told about a donor that was rolled back. No
/// publisher process exists yet, so the rows queue up with processed_at_utc null, ready for one.
/// </summary>
public sealed class OutboxWriter(
    DonDbContext context,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Write<TEvent>(string eventType, string aggregateType, Guid aggregateId, TEvent payload)
        where TEvent : class
    {
        context.OutboxMessages.Add(new OutboxMessage
        {
            OrganisationId = currentUser.OrganisationId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload, SerializerOptions),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            OccurredAtUtc = clock.UtcNow,
            ProcessedAtUtc = null,
            CorrelationId = currentUser.CorrelationId
        });
    }
}
