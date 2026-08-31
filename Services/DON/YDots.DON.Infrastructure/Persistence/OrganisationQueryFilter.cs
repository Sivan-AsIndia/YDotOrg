using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using YDots.DON.Domain.Common;

namespace YDots.DON.Infrastructure.Persistence;

/// <summary>
/// Builds the global query filter applied to every Organisation-owned entity.
///
/// WHY REFLECTION RATHER THAN A LINE PER ENTITY. There are fifteen Organisation-owned entities
/// and there will be more. A hand-written <c>HasQueryFilter</c> per entity is one line somebody
/// eventually forgets, and a missing filter is invisible. Driving it off the marker interface
/// means a new entity is isolated the moment it declares <see cref="IOrganisationOwned"/>.
///
/// WHY THE FILTER READS THROUGH THE CONTEXT. The expression closes over the DbContext instance
/// and reads the Organisation WHEN THE QUERY RUNS, not when the model is built. EF builds the
/// model once per application; the Organisation changes per request. Capturing the value
/// instead of the accessor would pin every request to whichever Organisation happened to make
/// the first one - a spectacular and very quiet data leak.
/// </summary>
internal static class OrganisationQueryFilter
{
    private static readonly MethodInfo BuildMethod =
        typeof(OrganisationQueryFilter).GetMethod(
            nameof(Build), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void Apply(ModelBuilder modelBuilder, Type clrType, DonDbContext context)
    {
        var filter = BuildMethod.MakeGenericMethod(clrType).Invoke(null, [context]) as LambdaExpression;

        modelBuilder.Entity(clrType).HasQueryFilter(filter!);
    }

    /// <summary>
    /// "OrganisationId == current".
    ///
    /// NOTE WHAT HAPPENS WHEN NOTHING IS RESOLVED: the context's Organisation is null, the
    /// comparison is false for every row, and the query returns nothing. That is the correct
    /// failure mode - an unresolved request sees no data at all, rather than seeing everybody's.
    ///
    /// THE COMPARISON IS AGAINST THE NULLABLE, deliberately, and NOT against <c>.Value</c>
    /// behind a <c>HasValue</c> guard. Inside an expression tree the guard protects nothing: EF
    /// evaluates every client-side subexpression while it builds the query parameters, so it
    /// reaches the <c>.Value</c> whatever the guard says and throws "Nullable object must have a
    /// value" on any request that has not resolved an Organisation.
    ///
    /// Comparing <c>Guid</c> to <c>Guid?</c> lifts the comparison, and a null yields SQL
    /// <c>organisation_id = NULL</c>, which is never true - the same "sees nothing" outcome,
    /// expressed in a way EF can actually translate.
    /// </summary>
    private static Expression<Func<TEntity, bool>> Build<TEntity>(DonDbContext context)
        where TEntity : class, IOrganisationOwned =>
        entity => entity.OrganisationId == context.TenantContext.OrganisationId;
}
