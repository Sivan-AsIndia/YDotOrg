namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// One commit per request. The DbContext implements it, so a handler can save without the
/// application layer ever naming EF Core.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits the tracked changes. Audit columns and Tenant ownership are stamped inside
    /// this call, so a handler never has to set them.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
