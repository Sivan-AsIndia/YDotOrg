namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>
/// One commit per request.
///
/// IT IS LOAD-BEARING IN THIS SERVICE. Recording a donation writes the donation, updates the
/// intent, updates the attempt and writes an audit row - and any of those committing without
/// the others leaves the books wrong. One unit of work means all four land or none do.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a body inside an explicit database transaction.
    ///
    /// USED ONLY WHERE A SAVE IS NOT ENOUGH: applying a gateway event has to read the current
    /// state, decide, and write - and two webhooks for the same payment arriving at once would
    /// otherwise both read "not yet paid" and both record a donation. The transaction plus the
    /// unique constraint on the gateway event id is what makes that impossible rather than
    /// merely unlikely.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
}
