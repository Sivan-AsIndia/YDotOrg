using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using YDot.PAY.Domain.Common;

namespace YDot.PAY.Infrastructure.Persistence;

/// <summary>
/// Builds the global query filter applied to every Organisation-owned entity.
///
/// WHY THE FILTER READS THROUGH THE CONTEXT. The expression closes over the DbContext instance
/// and reads the Organisation WHEN THE QUERY RUNS, not when the model is built. EF builds the
/// model once per application; the Organisation changes per request. Capturing the value would
/// pin every request to whichever Organisation happened to make the first one - which on a
/// donations table would show one charity another charity's income for the life of the process.
/// </summary>
internal static class TenantQueryFilter
{
    private static readonly MethodInfo BuildMethod =
        typeof(TenantQueryFilter).GetMethod(
            nameof(Build), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void Apply(ModelBuilder modelBuilder, Type clrType, PaymentDbContext context)
    {
        var filter = BuildMethod.MakeGenericMethod(clrType).Invoke(null, [context]) as LambdaExpression;

        modelBuilder.Entity(clrType).HasQueryFilter(filter!);
    }

    /// <summary>
    /// "TenantId == current".
    ///
    /// An unresolved request sees NOTHING rather than everything, which is the correct failure
    /// mode: a query with no Organisation returns an empty page instead of the whole platform's
    /// donations.
    ///
    /// THE COMPARISON IS AGAINST THE NULLABLE, deliberately. Inside an expression tree a
    /// <c>HasValue</c> guard protects nothing - EF evaluates the client-side subexpression while
    /// building parameters and reaches the <c>.Value</c> anyway, throwing on every request that
    /// has not resolved an Organisation. Comparing <c>Guid</c> to <c>Guid?</c> lifts it, and a
    /// null yields SQL <c>tenant_id = NULL</c>, which is never true.
    /// </summary>
    private static Expression<Func<TEntity, bool>> Build<TEntity>(PaymentDbContext context)
        where TEntity : class, ITenantOwned =>
        entity => entity.TenantId == context.TenantContext.TenantId;
}
