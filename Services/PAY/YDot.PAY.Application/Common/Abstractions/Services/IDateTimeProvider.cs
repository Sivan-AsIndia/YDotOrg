namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// The clock, behind an interface.
///
/// Everything is UTC. A donation timestamp decides which financial year its receipt falls in,
/// and a local time on a database shared by four services is a receipt numbered into the wrong
/// year the first time somebody deploys to another region.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayUtc { get; }

    /// <summary>
    /// The financial year a moment falls in, as "2026-27".
    ///
    /// On the interface rather than in a handler because receipt numbering, tax reporting and
    /// the register's default filter all need the same answer, and three implementations of a
    /// year boundary is two too many.
    /// </summary>
    string FinancialYearFor(DateTimeOffset moment);
}
