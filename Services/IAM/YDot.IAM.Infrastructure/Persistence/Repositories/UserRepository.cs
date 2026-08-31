using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to the <see cref="User"/> aggregate.
///
/// MOST OF THESE METHODS LOOK LIKE THEY FORGOT THE TENANT FILTER. They did not — the global
/// query filter on <c>IamDbContext</c> has already restricted the set before any of this code
/// runs, so <c>GetByIdAsync</c> returns null for a user in another Organisation rather than
/// returning them.
///
/// The methods that DO name an Organisation explicitly are the ones the filter cannot serve:
/// a sign-in lookup that happens before authentication, and a uniqueness probe that has to
/// look across the filter to answer honestly.
/// </summary>
public sealed class UserRepository(IamDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <summary>
    /// A user by id, inside one named Organisation.
    ///
    /// <c>IgnoreQueryFilters</c> again, and for the same reason as the sign-in lookup below: this
    /// runs BEFORE authentication, so the ambient Organisation is null and the invited user is
    /// invisible to the filter. The Organisation comes from the invitation row - which was itself
    /// found by an unguessable token hash - and is re-applied here explicitly.
    ///
    /// A Guid.Empty tenant means a platform-level invitation, which matches only a user with no
    /// Organisation. Comparing against Guid.Empty directly would match nobody, which is how a
    /// SuperAdmin invitation would silently never work.
    /// </summary>
    public Task<User?> FindByIdInTenantAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .Where(user => tenantId == Guid.Empty
                ? user.TenantId == null
                : user.TenantId == tenantId)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <summary>
    /// Loads the aggregate with everything the effective-access resolver needs, in one round
    /// trip. Used on the sign-in path, where a second query per role would be a query per
    /// authentication.
    /// </summary>
    public Task<User?> GetWithAccessAsync(Guid id, CancellationToken cancellationToken) =>
        context.Users
            .Include(user => user.UserRoles).ThenInclude(assignment => assignment.Role)
                .ThenInclude(role => role!.RolePermissions)
            .Include(user => user.DataScopes)
            .Include(user => user.Claims)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <summary>
    /// Finds a user for sign-in, inside ONE named Organisation.
    ///
    /// <c>IgnoreQueryFilters</c> is used here deliberately and is one of only a handful of
    /// places in the codebase that does. It has to be: this runs BEFORE authentication, when
    /// the ambient Organisation was resolved from a host name and the caller has proved
    /// nothing at all. The filter is then re-applied by hand as an explicit
    /// <c>TenantId == tenantId</c> — the same restriction, but written where a reviewer can
    /// see it rather than inherited invisibly.
    ///
    /// A null <paramref name="tenantId"/> means the global scope and matches only SuperAdmin.
    /// </summary>
    public Task<User?> FindForSignInAsync(
        string normalizedIdentifier, Guid? tenantId, CancellationToken cancellationToken)
    {
        var identifier = normalizedIdentifier.ToUpperInvariant();

        return context.Users
            .IgnoreQueryFilters()
            .Where(user => tenantId.HasValue
                ? user.TenantId == tenantId.Value
                : user.TenantId == null && user.IsSuperAdmin)
            .FirstOrDefaultAsync(
                user => user.NormalizedEmail == identifier || user.NormalizedUserName == identifier,
                cancellationToken);
    }

    /// <summary>
    /// The global root user. Filters bypassed for the same reason as above, and narrowed by
    /// <c>IsSuperAdmin</c> so an ordinary user can never be returned by this path.
    /// </summary>
    public Task<User?> FindSuperAdminAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var identifier = normalizedEmail.ToUpperInvariant();

        return context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == null && user.IsSuperAdmin)
            .FirstOrDefaultAsync(
                user => user.NormalizedEmail == identifier || user.NormalizedUserName == identifier,
                cancellationToken);
    }

    /// <summary>
    /// Is this address already used inside this Organisation?
    ///
    /// Filters bypassed, then narrowed to the one Organisation. That is the whole point: the
    /// answer must be about THIS Organisation and no other, and the same address in a
    /// different Organisation must not report a conflict.
    /// </summary>
    public Task<bool> EmailExistsAsync(
        string normalizedEmail, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId)
            .Where(user => excludingUserId == null || user.Id != excludingUserId)
            .AnyAsync(user => user.NormalizedEmail == normalizedEmail.ToUpperInvariant(), cancellationToken);

    public Task<bool> UsernameExistsAsync(
        string normalizedUsername, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId)
            .Where(user => excludingUserId == null || user.Id != excludingUserId)
            .AnyAsync(user => user.NormalizedUserName == normalizedUsername.ToUpperInvariant(), cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId)
            .Where(user => excludingUserId == null || user.Id != excludingUserId)
            .AnyAsync(user => user.Code == code, cancellationToken);

    /// <summary>
    /// The next sequential user code for an Organisation, USR-00042.
    ///
    /// Derived from the current count rather than a database sequence, because the number is
    /// per Organisation and a shared sequence would leave gaps that look like deleted people.
    /// The collision loop covers the race between two concurrent creates; the unique index is
    /// what actually guarantees correctness.
    /// </summary>
    public async Task<string> NextUserCodeAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var existing = await context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId)
            .CountAsync(cancellationToken);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = string.Create(
                CultureInfo.InvariantCulture, $"USR-{existing + attempt:D5}");

            if (!await CodeExistsAsync(candidate, tenantId, null, cancellationToken))
            {
                return candidate;
            }
        }

        // Every sequential candidate was taken, which means heavy concurrency. A short random
        // suffix keeps the create working rather than failing on a cosmetic field.
        return string.Create(CultureInfo.InvariantCulture, $"USR-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
    }

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .CountAsync(
                user => user.TenantId == tenantId
                        && user.Status != UserStatus.Deactivated
                        && user.Status != UserStatus.Withdrawn,
                cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await context.Users.AddAsync(user, cancellationToken);

    public void Remove(User user) => context.Users.Remove(user);

    public async Task<IReadOnlyList<User>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Users
            .Where(user => ids.Contains(user.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountDirectReportsAsync(Guid managerUserId, CancellationToken cancellationToken) =>
        context.Users.CountAsync(user => user.ManagerUserId == managerUserId, cancellationToken);

    /// <summary>
    /// The Organisation first administrator.
    ///
    /// Filters bypassed because this is called from the PLATFORM review path: SuperAdmin
    /// approving TEN002 has not selected into it, so the ambient filter would exclude exactly
    /// the row being looked for. The Organisation is named explicitly instead.
    /// </summary>
    public Task<User?> FindTenantAdminAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId && user.IsTenantAdmin)
            .OrderBy(user => user.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountForTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .CountAsync(user => user.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName, user.UserName })
            .ToListAsync(cancellationToken);

        // Falls back through the names we might have, and finally to a dash rather than an
        // empty cell: a blank in an audit column reads as missing data rather than as an
        // account with no display name set.
        return rows.ToDictionary(
            row => row.Id,
            row => !string.IsNullOrWhiteSpace(row.DisplayName)
                ? row.DisplayName
                : !string.IsNullOrWhiteSpace(row.UserName) ? row.UserName : "—");
    }
}
