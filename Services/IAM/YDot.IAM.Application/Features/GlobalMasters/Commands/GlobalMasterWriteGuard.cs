using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Application.Features.GlobalMasters.Commands;

/// <summary>
/// The three questions every master write has to answer before it does anything, in one
/// place.
///
/// WHY THIS EXISTS RATHER THAN FIVE COPIES OF THE SAME THREE CHECKS. There are five masters
/// and each has create, update, activate, deactivate and delete - twenty-five write paths.
/// The scope rule, the platform-row rule and the version check are identical on all of them,
/// and the one that would eventually be written wrongly is the one nobody reviews closely.
/// Concentrating them here means a reviewer reads the rule once and every path is bound by
/// the same reading.
///
/// IT IS A GUARD, NOT AN AUTHORIZATION CHECK. Whether the caller holds
/// <c>gm.countries.edit</c> is decided by the <c>[HasPermission]</c> attribute on the
/// endpoint, before any of this runs. What this adds is the part a permission cannot express:
/// that holding the edit permission lets you edit YOUR OWN rows and never the shared
/// catalogue.
/// </summary>
public sealed class GlobalMasterWriteGuard(ITenantContext tenantContext, ICurrentUser currentUser)
{
    /// <summary>
    /// The scope a NEW row lands in.
    ///
    /// It is simply the resolved Organisation, which makes the behaviour follow the rule the
    /// rest of IAM already uses: a caller writes into whatever Organisation they are currently
    /// operating in. For a Tenant user that is always their own. For SuperAdmin it is whichever
    /// Organisation they selected - and null, meaning the shared platform catalogue, when they
    /// have selected none.
    ///
    /// So maintaining the ISO catalogue is done by a root user in platform mode, and it cannot
    /// be done by accident from inside an Organisation.
    /// </summary>
    public Guid? WriteScopeTenantId => tenantContext.TenantId;

    public Guid BusinessUnitId => tenantContext.BusinessUnitId;

    public bool IsSuperAdmin => currentUser.IsSuperAdmin;

    /// <summary>
    /// Refuses a write against a row the caller does not own.
    ///
    /// A platform row is read-only to everybody but a root user. The query filter already
    /// stops the caller seeing ANOTHER Organisation's rows, so the only thing left to guard is
    /// the shared catalogue - which every Organisation can see precisely because they all need
    /// to read it.
    ///
    /// Returns 403 rather than 404. The row is genuinely visible to the caller, so pretending
    /// it does not exist would be a lie that makes the screen harder to reason about; the
    /// honest answer is that it exists and is not theirs to change.
    /// </summary>
    public Result EnsureWritable(GlobalMasterEntity entity, string recordLabel)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.IsPlatformRow && !currentUser.IsSuperAdmin)
        {
            return Result.Failure(Error.Forbidden(
                $"{recordLabel} belongs to the shared platform catalogue and cannot be changed here. "
                + "Add an entry of your own instead."));
        }

        return Result.Success();
    }

    /// <summary>
    /// The optimistic concurrency check.
    ///
    /// Two administrators with the same country list open is the ordinary case, not the
    /// unusual one, and the failure mode without this is silent: the second save wins and the
    /// first person's change disappears with no error anywhere.
    /// </summary>
    public static Result EnsureVersionMatches(GlobalMasterEntity entity, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return entity.Version == expectedVersion
            ? Result.Success()
            : Result.Failure(Error.Concurrency());
    }

    /// <summary>
    /// Refuses a delete while anything still points at the row.
    ///
    /// 409 with a countable reason rather than a cascade. Removing a state that has cities
    /// beneath it would orphan every address in them, and the operator almost always meant
    /// "retire this", which deactivation does without destroying anything.
    /// </summary>
    public static Result EnsureNoDependents(int dependentCount, string recordLabel, string dependentLabel)
    {
        return dependentCount == 0
            ? Result.Success()
            : Result.Failure(Error.InUse(
                $"{recordLabel} still has {dependentCount} {dependentLabel} linked to it. "
                + "Move or remove them first, or deactivate this record instead."));
    }
}
