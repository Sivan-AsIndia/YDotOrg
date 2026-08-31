using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// Remembers the Idempotency-Key values already processed (section 10). A webhook or import
/// that retries after an uncertain response gets the original reference back instead of a
/// second record.
/// </summary>
public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> FindAsync(string key, string endpoint, CancellationToken cancellationToken = default);

    void Add(IdempotencyRecord record);
}
