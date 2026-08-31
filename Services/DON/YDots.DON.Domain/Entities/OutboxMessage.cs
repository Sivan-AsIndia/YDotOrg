using YDots.DON.Domain.Common;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// One integration event waiting to be published (table don_outbox_messages).
///
/// The row is written inside the same SaveChanges as the business change, which is the whole
/// point: either both land or neither does, so another section can never be told about a donor
/// that was not actually saved. Section 10 names DonorCreatedV1 and DonorStatusChangedV1.
/// </summary>
public class OutboxMessage : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    /// <summary>Contract name of the event, for example DonorCreatedV1.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>JSON body of the event. Redacted: no contact values, no evidence, no secrets.</summary>
    public string Payload { get; set; } = string.Empty;

    public string AggregateType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Null until a publisher has taken the row. No publisher runs yet; the rows queue up.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
