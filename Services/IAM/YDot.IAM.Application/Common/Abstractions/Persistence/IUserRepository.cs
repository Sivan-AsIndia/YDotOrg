using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the <see cref="User"/> aggregate.
///
/// EVERY METHOD HERE IS ALREADY TENANT-SCOPED, and not because each one remembers to add a
/// Where clause. The global query filter on <c>IamDbContext</c> restricts the underlying set
/// to the current Organisation before any of this code runs, so <c>GetByIdAsync</c> returns
/// null for a user in another Organisation rather than returning them. The explicit Tenant
/// arguments that do appear below are the ones the filter cannot express — a uniqueness probe
/// that has to look ACROSS the filter, or a lookup during sign-in when no Organisation has
/// been selected yet.
/// </summary>
public interface IUserRepository
{
    /// <summary>Load a tracked aggregate by identifier, inside the caller Organisation.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a user by id inside ONE named Organisation, bypassing the ambient filter.
    ///
    /// WHY THIS EXISTS. The invitation and password-recovery flows are both ANONYMOUS: somebody
    /// holding a link has proved nothing and no Organisation has been resolved from the host, so
    /// the ambient filter is "TenantId == null" and <see cref="GetByIdAsync"/> cannot see the
    /// invited user at all. Every screen in the invitation flow reported "that invitation link is
    /// not valid" for an invitation that was perfectly valid - found, pending, unexpired, with a
    /// real user behind it - and forgotten-password had the same fault for the same reason.
    ///
    /// IT IS SAFE FOR THE SAME REASON <c>FindForSignInAsync</c> IS. The caller has already produced
    /// an unguessable token, the invitation row was located by its hash, and the Organisation is
    /// taken FROM THAT ROW rather than from anything the caller sent. The restriction is then
    /// re-applied by hand, so it is visible to a reviewer rather than inherited invisibly.
    /// </summary>
    Task<User?> FindByIdInTenantAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Load with roles, data scopes and claims, for building the effective access set.</summary>
    Task<User?> GetWithAccessAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Find by e-mail or username INSIDE ONE NAMED ORGANISATION, for sign-in.
    ///
    /// The Organisation is passed explicitly rather than taken from the ambient context
    /// because this runs before authentication, when the only thing that resolved the Tenant
    /// was the host name. Passing it makes the Organisation being authenticated against
    /// visible at the call site, which is exactly where it should be reviewable.
    ///
    /// <paramref name="tenantId"/> null means the global scope, and matches only the
    /// SuperAdmin record.
    /// </summary>
    Task<User?> FindForSignInAsync(string normalizedIdentifier, Guid? tenantId, CancellationToken cancellationToken);

    /// <summary>The global root user, looked up across every Organisation.</summary>
    Task<User?> FindSuperAdminAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Is this e-mail already used inside this Organisation?
    ///
    /// Scoped on purpose: the same address existing in a different Organisation is not a
    /// conflict, it is the documented behaviour. <paramref name="excludingUserId"/> lets an
    /// edit exclude the row being edited.
    /// </summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string normalizedUsername, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken);

    /// <summary>Next sequential user code for an Organisation, for example USR-00042.</summary>
    Task<string> NextUserCodeAsync(Guid? tenantId, CancellationToken cancellationToken);

    /// <summary>How many users an Organisation currently has, for the licence ceiling.</summary>
    Task<int> CountActiveAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    void Remove(User user);

    /// <summary>Load several by id in one round trip, for the bulk screens.</summary>
    Task<IReadOnlyList<User>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Users reporting to this manager, used to block a delete that would orphan them.</summary>
    Task<int> CountDirectReportsAsync(Guid managerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// The Organisation first administrator - the person onboarding e-mails are addressed to.
    ///
    /// The Organisation is passed explicitly because this is called from the platform review
    /// path, where SuperAdmin is acting on an Organisation they have not selected into and the
    /// ambient filter would therefore not match it.
    /// </summary>
    Task<User?> FindTenantAdminAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Users in an Organisation, for the platform directory row counts.</summary>
    Task<int> CountForTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Display names for a set of user ids, in one query.
    ///
    /// A screen that says "uploaded by 8f2c1d…" is showing a database key to a person. Resolving
    /// the whole set at once rather than a lookup per row is the difference between one query and
    /// one per file on a submission.
    ///
    /// Query filters are ignored: a reviewer works from the platform host with no Organisation
    /// selected, and would otherwise see every name come back blank. Only ids already found on
    /// records the caller is permitted to read are ever passed in.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
