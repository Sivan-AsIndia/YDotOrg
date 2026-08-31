using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Services;

/// <summary>
/// Resolves what a user can actually do, from every source at once.
///
/// WHY THIS IS ONE SERVICE AND NOT FOUR QUERIES. Access arrives from four places — active
/// role assignments, direct user claims, data scopes, and the blanket grant a Tenant-wide or
/// SuperAdmin flag confers. Resolving them in one place means the token builder and the
/// IAM-USR-03 preview screen can never disagree, which they would the moment two code paths
/// each did their own version of the union.
///
/// THE PRECEDENCE RULES, IN ORDER:
///
/// <code>
/// 1. SuperAdmin              -> everything, full stop
/// 2. GrantsAllTenantPermissions -> every Tenant permission in the catalogue
/// 3. explicit deny           -> beats any allow below
/// 4. role permissions        -> union of every ACTIVE, in-window assignment
/// 5. direct user claims      -> added on top
/// </code>
///
/// Deny beating allow is what makes it possible to carve one permission out of a broad role
/// without unpicking the role.
/// </summary>
public interface IEffectiveAccessService
{
    /// <summary>
    /// The full picture for one user, inside the Organisation they are operating in.
    ///
    /// <paramref name="operatingTenantId"/> matters for SuperAdmin: it is the Organisation
    /// they selected, and it decides which Tenant-scoped roles are even considered. For an
    /// ordinary user it is simply their own.
    /// </summary>
    Task<EffectiveAccess> ResolveAsync(
        Guid userId, Guid? operatingTenantId, CancellationToken cancellationToken);

    /// <summary>Same, from an already-loaded aggregate, to avoid a second round trip on sign-in.</summary>
    Task<EffectiveAccess> ResolveAsync(
        User user, Guid? operatingTenantId, CancellationToken cancellationToken);

    /// <summary>
    /// What the access would become if these roles were assigned instead of the current ones.
    ///
    /// Used by the preview screen so an administrator sees the consequence before committing.
    /// Adding a role to somebody who already holds three is not obviously safe: it may
    /// overlap entirely, or quietly hand over an export permission nobody intended.
    /// </summary>
    Task<AccessComparison> PreviewAsync(
        Guid userId,
        Guid? operatingTenantId,
        IReadOnlyCollection<Guid> proposedRoleIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a proposed set of roles against the segregation-of-duties rules. Returns the
    /// blocking conflicts, so the assignment screen can refuse the combination at the point
    /// somebody tries to create it rather than at the next audit.
    /// </summary>
    Task<IReadOnlyList<string>> CheckSegregationOfDutiesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> proposedRoleIds,
        CancellationToken cancellationToken);
}
