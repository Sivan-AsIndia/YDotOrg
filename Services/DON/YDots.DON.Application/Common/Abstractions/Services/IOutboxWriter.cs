namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>
/// Stages one integration event on the current unit of work (section 10).
///
/// "Written to outbox only when another section needs the fact" — so the two events named in
/// the contract, DonorCreatedV1 and DonorStatusChangedV1, are the only ones any handler writes.
/// </summary>
public interface IOutboxWriter
{
    void Write<TEvent>(string eventType, string aggregateType, Guid aggregateId, TEvent payload)
        where TEvent : class;
}
