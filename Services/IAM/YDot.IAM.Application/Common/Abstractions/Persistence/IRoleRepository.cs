using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>Write-side access to roles, their permissions and their assignments.</summary>
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Load with permissions and menu mappings, for the role editor.</summary>
    Task<Role?> GetWithPermissionsAsync(Guid id, CancellationToken cancellationToken);

    Task<Role?> GetByCodeAsync(string code, Guid? tenantId, CancellationToken cancellationToken);

    /// <summary>The role a new user gets when none is chosen.</summary>
    Task<Role?> GetDefaultRoleAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, Guid? tenantId, Guid? excludingRoleId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string normalizedName, Guid? tenantId, Guid? excludingRoleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every assignable role in the Organisation, for pickers.</summary>
    Task<IReadOnlyList<Role>> GetAssignableAsync(Guid? tenantId, CancellationToken cancellationToken);

    Task AddAsync(Role role, CancellationToken cancellationToken);

    void Remove(Role role);

    /// <summary>
    /// How many people hold this role. A role with holders cannot be deleted — it is
    /// deactivated instead, so the historical assignments keep resolving.
    /// </summary>
    Task<int> CountAssignmentsAsync(Guid roleId, CancellationToken cancellationToken);

    // ---- Role permissions -------------------------------------------------------------------

    Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);

    Task AddRolePermissionAsync(RolePermission rolePermission, CancellationToken cancellationToken);

    void RemoveRolePermissions(IEnumerable<RolePermission> rolePermissions);

    // ---- User assignments -------------------------------------------------------------------------

    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// A user's role assignments inside ONE named Organisation, bypassing the ambient filter.
    ///
    /// SAME REASON AS <c>IUserRepository.FindByIdInTenantAsync</c>. Invitation acceptance is
    /// anonymous, so the ambient Organisation is null and <see cref="GetUserRolesAsync"/> returns
    /// an empty list for a user who plainly has roles. The "already assigned?" guard in
    /// AssignInitialRoleAsync then saw nothing, tried to grant the role a second time, and the
    /// unique index ix_iam_user_roles_active_unique refused it - so activating an account died
    /// with a 500 after the organisation had already been created.
    /// </summary>
    Task<IReadOnlyList<UserRole>> GetUserRolesInTenantAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken);

    /// <summary>The live assignment for one pair, or null. Used to avoid a duplicate grant.</summary>
    Task<UserRole?> GetActiveAssignmentAsync(Guid userId, Guid roleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserRole>> GetRoleMembersAsync(Guid roleId, CancellationToken cancellationToken);

    Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken);

    // ---- Segregation of duties ---------------------------------------------------------------------

    /// <summary>
    /// Rules naming any of the given roles, in either direction. The assignment handler calls
    /// this before granting, so a conflicting combination is refused at the point somebody
    /// tries to create it rather than at the next audit.
    /// </summary>
    Task<IReadOnlyList<RoleIncompatibility>> GetIncompatibilitiesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    Task AddIncompatibilityAsync(RoleIncompatibility incompatibility, CancellationToken cancellationToken);

    void RemoveIncompatibility(RoleIncompatibility incompatibility);

    Task<RoleIncompatibility?> GetIncompatibilityAsync(Guid id, CancellationToken cancellationToken);

    // ---- Role claims ----------------------------------------------------------------------------------

    Task<IReadOnlyList<RoleClaimEntry>> GetRoleClaimsAsync(Guid roleId, CancellationToken cancellationToken);

    Task AddRoleClaimAsync(RoleClaimEntry claim, CancellationToken cancellationToken);

    void RemoveRoleClaims(IEnumerable<RoleClaimEntry> claims);
}

/// <summary>
/// The global permission catalogue. Read-mostly: the rows are seeded from
/// <c>PermissionCodes</c> and <c>ModulePermissionCatalogue</c> rather than authored by hand.
/// </summary>
public interface IPermissionRepository
{
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Permission>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    Task<IReadOnlyList<Permission>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Everything a Tenant role is allowed to carry: the active codes, minus the
    /// platform-only ones. This is what stops a role edit handing a TenantAdmin the ability
    /// to create or approve Organisations.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetTenantAssignableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Permission>> GetByModuleAsync(string moduleCode, CancellationToken cancellationToken);

    Task AddAsync(Permission permission, CancellationToken cancellationToken);

    /// <summary>
    /// The effective permission codes for one user: the union of every active role
    /// assignment, plus direct claims, minus anything explicitly denied.
    /// </summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(Guid userId, CancellationToken cancellationToken);
}
