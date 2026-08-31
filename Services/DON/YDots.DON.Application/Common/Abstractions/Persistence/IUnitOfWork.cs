namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// One commit boundary, exactly as section 6 requires: "Commit aggregate and outbox atomically."
/// Every handler changes tracked entities and then calls this once.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
