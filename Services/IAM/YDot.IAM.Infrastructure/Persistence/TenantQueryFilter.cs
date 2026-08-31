using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Infrastructure.Persistence;

/// <summary>
/// Builds the global query filter expressions applied to every Tenant-owned entity.
///
/// WHY REFLECTION RATHER THAN A LINE PER ENTITY. There are around thirty Tenant-owned
/// entities and there will be more. A hand-written <c>HasQueryFilter</c> per entity is one
/// line somebody eventually forgets to add, and a missing filter is invisible — the code
/// compiles, the tests pass, and one Organisation quietly reads another. Driving it off the
/// marker interface means a new entity is isolated the moment it declares
/// <see cref="ITenantOwned"/>, with nothing to remember.
///
/// WHY THE FILTER READS THROUGH THE CONTEXT. The expression closes over the DbContext
/// instance and reads <c>TenantContext.TenantId</c> when the query runs, not when the model
/// is built. EF builds the model once per application; the Organisation changes per request.
/// Capturing the value instead of the accessor would pin every request to whichever
/// Organisation happened to make the first one — a spectacular and very quiet data leak.
/// </summary>
internal static class TenantQueryFilter
{
    private static readonly MethodInfo StrictMethod =
        typeof(TenantQueryFilter).GetMethod(nameof(BuildStrict), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ScopedMethod =
        typeof(TenantQueryFilter).GetMethod(nameof(BuildScoped), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// "TenantId == current". For entities that belong to exactly one Organisation and have
    /// no global form.
    /// </summary>
    internal static void ApplyStrict(ModelBuilder modelBuilder, Type clrType, IamDbContext context)
    {
        var filter = StrictMethod.MakeGenericMethod(clrType).Invoke(null, [context]) as LambdaExpression;
        modelBuilder.Entity(clrType).HasQueryFilter(filter!);
    }

    /// <summary>
    /// "TenantId == current OR TenantId IS NULL". For <c>User</c> and <c>Role</c> only, so
    /// the global SuperAdmin record and the platform roles stay reachable while an
    /// Organisation is selected.
    /// </summary>
    internal static void ApplyScoped(ModelBuilder modelBuilder, Type clrType, IamDbContext context)
    {
        var filter = ScopedMethod.MakeGenericMethod(clrType).Invoke(null, [context]) as LambdaExpression;
        modelBuilder.Entity(clrType).HasQueryFilter(filter!);
    }

    /// <summary>
    /// The strict filter.
    ///
    /// Note what happens when no Organisation is resolved: <c>CurrentTenantId</c> is null,
    /// the comparison is false for every row, and the query returns nothing. That is the
    /// correct failure mode — an unresolved request sees no Tenant data at all, rather than
    /// seeing everybody data.
    ///
    /// <c>IsGlobalQueryScope</c> is the one bypass, and it is deliberately narrow: it is set
    /// only for the genuinely platform-wide reads (SuperAdmin listing every Organisation),
    /// never merely because the caller is SuperAdmin. A root user operating inside TEN001 is
    /// filtered to TEN001 exactly like anybody else.
    ///
    /// THE COMPARISON IS AGAINST THE NULLABLE, deliberately, and NOT against <c>.Value</c>
    /// behind a <c>HasValue</c> guard. Inside an expression tree the guard does not protect
    /// anything: EF evaluates every client-side subexpression while it builds the query
    /// parameters, so it reaches the <c>.Value</c> whatever the guard says and throws
    /// "Nullable object must have a value" on any request that has not resolved an
    /// Organisation — which is every SuperAdmin request made before one is selected.
    ///
    /// Comparing <c>Guid</c> to <c>Guid?</c> lifts the comparison, and a null yields SQL
    /// <c>tenant_id = NULL</c>, which is never true. That is the same "sees nothing" outcome
    /// the guard was written to produce, expressed in a way EF can actually translate.
    /// </summary>
    private static Expression<Func<TEntity, bool>> BuildStrict<TEntity>(IamDbContext context)
        where TEntity : class, ITenantOwned =>
        entity => context.TenantContext.IsGlobalQueryScope
                  || entity.TenantId == context.TenantContext.TenantId;

    /// <summary>
    /// The widened filter for <c>User</c> and <c>Role</c>.
    ///
    /// The "OR TenantId IS NULL" arm is a real, deliberate hole in the isolation, and it is
    /// exactly two tables wide. It exists because the brief requires SuperAdmin to have
    /// <c>TenantId = NULL</c> and still be loadable while operating inside an Organisation —
    /// without it, a root user selecting TEN001 could no longer read their own user record.
    ///
    /// It is safe because a null TenantId is only ever held by rows that belong to nobody:
    /// the root user and the platform roles. A check constraint on <c>iam_users</c> enforces
    /// that a null TenantId implies <c>is_super_admin</c>, so an ordinary Tenant user cannot
    /// be given a null and become visible everywhere.
    /// </summary>
    private static Expression<Func<TEntity, bool>> BuildScoped<TEntity>(IamDbContext context)
        where TEntity : class, ITenantScoped =>
        entity => context.TenantContext.IsGlobalQueryScope
                  || entity.TenantId == null
                  || entity.TenantId == context.TenantContext.TenantId;
}
