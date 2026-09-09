using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>Invitations, the Tenant-specific front door for a new account.</summary>
public interface IInvitationRepository
{
    Task<UserInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve an invitation from the token in the link.
    ///
    /// Looked up ACROSS Organisations on purpose: the person clicking has no session, so
    /// there is no ambient Tenant. The row itself names the Organisation, and everything
    /// downstream acts on THAT — which is precisely what stops an invitation for TEN001
    /// activating the unrelated same-address user in TEN002.
    /// </summary>
    Task<UserInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The outstanding invitation for one user, if any.</summary>
    Task<UserInvitation?> GetPendingForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserInvitation>> GetPendingForTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(UserInvitation invitation, CancellationToken cancellationToken);

    /// <summary>Marks lapsed invitations expired. Idempotent, safe to run repeatedly.</summary>
    Task<int> ExpireOverdueAsync(DateTimeOffset asOf, CancellationToken cancellationToken);
}

/// <summary>The dynamic navigation tree: catalogue, Organisation overrides and role mappings.</summary>
public interface IMenuRepository
{
    Task<IReadOnlyList<MenuDefinition>> GetCatalogueAsync(CancellationToken cancellationToken);

    Task<MenuDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken);

    Task<MenuDefinition?> GetDefinitionByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a code is already taken WITHIN ONE OWNER.
    ///
    /// The owner is a parameter rather than an assumption because a menu code is only unique
    /// inside the set it belongs to: null asks about the platform catalogue, an id asks about one
    /// Organisation's own nodes. Two charities both calling something REPORTS is not a collision,
    /// and answering as though it were would tell one of them what the other had named.
    /// </summary>
    Task<bool> DefinitionCodeExistsAsync(
        string code, Guid? excludingId, Guid? ownerTenantId, CancellationToken cancellationToken);

    Task AddDefinitionAsync(MenuDefinition definition, CancellationToken cancellationToken);

    void RemoveDefinition(MenuDefinition definition);

    /// <summary>
    /// Removes the Organisation settings and role mappings that point at one of its OWN nodes.
    ///
    /// WHY DELETING A NODE NEEDS THIS AT ALL. Creating a node immediately produces rows that
    /// reference it - a TenantMenu row per Organisation setting, a RoleMenu row per role mapped
    /// to it - so the reference guard that protects a platform node made an Organisation's own
    /// node permanently undeletable from the moment it existed. "Retire it instead" is the right
    /// answer for a node other Organisations also use, and no answer at all for one nobody else
    /// has ever seen.
    ///
    /// SCOPED TO THE OWNER, so this can never reach across Organisations. It is only ever called
    /// for an owned node, and the owner is passed rather than inferred so that is checkable here
    /// rather than only at the call site.
    /// </summary>
    Task CascadeDeleteOwnedDefinitionAsync(
        Guid menuDefinitionId, Guid ownerTenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantMenu>> GetTenantMenusAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantMenu?> GetTenantMenuAsync(Guid tenantId, Guid menuDefinitionId, CancellationToken cancellationToken);

    Task AddTenantMenuAsync(TenantMenu tenantMenu, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleMenu>> GetRoleMenusAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>Mappings for several roles at once, for building one caller navigation.</summary>
    Task<IReadOnlyList<RoleMenu>> GetRoleMenusForRolesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    Task AddRoleMenuAsync(RoleMenu roleMenu, CancellationToken cancellationToken);

    void RemoveRoleMenus(IEnumerable<RoleMenu> roleMenus);

    /// <summary>
    /// How many Organisation configurations and role mappings point at one catalogue node.
    ///
    /// Asked before deleting a node. A definition with dependants cannot simply be removed -
    /// the rows referencing it would be orphaned - so the delete is refused and the caller is
    /// told to retire it instead, which is the reversible operation that has the same effect
    /// on what people see.
    /// </summary>
    Task<int> CountDefinitionReferencesAsync(
        Guid menuDefinitionId, CancellationToken cancellationToken);
}

/// <summary>Departments and organisation units, the Tenant-owned structural masters.</summary>
public interface IOrganisationStructureRepository
{
    Task<Department?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Department>> GetDepartmentsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> DepartmentCodeExistsAsync(string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken);

    Task AddDepartmentAsync(Department department, CancellationToken cancellationToken);

    void RemoveDepartment(Department department);

    Task<int> CountDepartmentMembersAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<OrganisationUnit?> GetUnitAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganisationUnit>> GetUnitsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> UnitCodeExistsAsync(string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken);

    Task AddUnitAsync(OrganisationUnit unit, CancellationToken cancellationToken);

    void RemoveUnit(OrganisationUnit unit);

    Task<int> CountUnitMembersAsync(Guid unitId, CancellationToken cancellationToken);
}

/// <summary>Access requests and access reviews.</summary>
public interface IGovernanceRepository
{
    Task<AccessRequest?> GetAccessRequestAsync(Guid id, CancellationToken cancellationToken);

    Task<string> NextRequestNumberAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAccessRequestAsync(AccessRequest request, CancellationToken cancellationToken);

    Task<int> ExpireOverdueRequestsAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    Task<AccessReview?> GetAccessReviewAsync(Guid id, CancellationToken cancellationToken);

    Task<string> NextReviewNumberAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// A contiguous block of review numbers, reserved in one read.
    ///
    /// Raising a campaign creates every review in a single unit of work, so asking for the next
    /// number inside that loop asks a database that has not changed yet and returns the same
    /// number every time. The block is decided up front instead.
    /// </summary>
    Task<IReadOnlyList<string>> NextReviewNumbersAsync(
        Guid tenantId, int count, CancellationToken cancellationToken);

    Task AddAccessReviewAsync(AccessReview review, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessReview>> GetReviewsForCampaignAsync(Guid campaignId, CancellationToken cancellationToken);

    Task<AccessReviewCampaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CampaignCodeExistsAsync(string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken);

    Task AddCampaignAsync(AccessReviewCampaign campaign, CancellationToken cancellationToken);

    /// <summary>Flags reviews past their due date. Idempotent.</summary>
    Task<int> MarkOverdueReviewsAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    Task<LoginIdentifierChangeRequest?> GetIdentifierChangeAsync(Guid id, CancellationToken cancellationToken);

    Task<LoginIdentifierChangeRequest?> GetOpenIdentifierChangeAsync(Guid userId, CancellationToken cancellationToken);

    Task AddIdentifierChangeAsync(LoginIdentifierChangeRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserDataScope>> GetDataScopesAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserDataScope?> GetDataScopeAsync(Guid id, CancellationToken cancellationToken);

    Task AddDataScopeAsync(UserDataScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserClaimEntry>> GetUserClaimsAsync(Guid userId, CancellationToken cancellationToken);

    Task AddUserClaimAsync(UserClaimEntry claim, CancellationToken cancellationToken);

    void RemoveUserClaims(IEnumerable<UserClaimEntry> claims);
}

/// <summary>Bulk user administration jobs.</summary>
public interface IBulkOperationRepository
{
    Task<BulkOperation?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<BulkOperation?> GetWithItemsAsync(Guid id, CancellationToken cancellationToken);

    Task<string> NextOperationNumberAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(BulkOperation operation, CancellationToken cancellationToken);

    Task AddItemsAsync(IEnumerable<BulkOperationItem> items, CancellationToken cancellationToken);

    Task<IReadOnlyList<BulkOperationItem>> GetItemsAsync(Guid operationId, CancellationToken cancellationToken);
}

/// <summary>Append-only audit writing and the outbox.</summary>
public interface IAuditRepository
{
    Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    Task AddOutboxAsync(OutboxMessage message, CancellationToken cancellationToken);

    Task<IdempotencyRecord?> GetIdempotencyRecordAsync(
        string key, string endpoint, Guid? tenantId, CancellationToken cancellationToken);

    Task AddIdempotencyRecordAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}

/// <summary>
/// Dropdown data for the screens. Separate from the aggregate repositories because a lookup
/// is a projection — it never needs tracking, and loading whole aggregates to render a
/// select box is how a list screen ends up issuing forty queries.
/// </summary>
public interface ILookupRepository
{
    Task<IReadOnlyList<LookupItem>> GetRolesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> GetDepartmentsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> GetOrganisationUnitsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> GetManagersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> GetPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>Organisations SuperAdmin may select. Empty for a Tenant user.</summary>
    Task<IReadOnlyList<LookupItem>> GetSelectableTenantsAsync(CancellationToken cancellationToken);
}
