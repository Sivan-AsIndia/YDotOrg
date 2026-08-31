using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>Invitations: the Tenant-specific front door for a new account.</summary>
public sealed class InvitationRepository(IamDbContext context) : IInvitationRepository
{
    public Task<UserInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.UserInvitations.FirstOrDefaultAsync(invitation => invitation.Id == id, cancellationToken);

    /// <summary>
    /// Resolves an invitation from the token in the link.
    ///
    /// Filters bypassed, and necessarily so: the person clicking has no session, so there is
    /// no ambient Organisation to filter by. The ROW names the Organisation and the user, and
    /// acceptance acts on those — which is precisely what stops an invitation for TEN001 ever
    /// touching the same address in TEN002.
    /// </summary>
    public Task<UserInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.UserInvitations
            .IgnoreQueryFilters()
            .Include(invitation => invitation.User)
            .Include(invitation => invitation.Tenant)
            .FirstOrDefaultAsync(invitation => invitation.TokenHash == tokenHash, cancellationToken);

    public Task<UserInvitation?> GetPendingForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        context.UserInvitations
            .IgnoreQueryFilters()
            .Where(invitation => invitation.UserId == userId)
            .Where(invitation => invitation.Status == InvitationStatus.Pending
                                 || invitation.Status == InvitationStatus.Resent)
            .OrderByDescending(invitation => invitation.InvitedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<UserInvitation>> GetPendingForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.UserInvitations
            .IgnoreQueryFilters()
            .Include(invitation => invitation.User)
            .Where(invitation => invitation.TenantId == tenantId)
            .Where(invitation => invitation.Status == InvitationStatus.Pending
                                 || invitation.Status == InvitationStatus.Resent)
            .OrderByDescending(invitation => invitation.InvitedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(UserInvitation invitation, CancellationToken cancellationToken) =>
        await context.UserInvitations.AddAsync(invitation, cancellationToken);

    /// <summary>Marks lapsed invitations expired. Idempotent, so it is safe to run repeatedly.</summary>
    public async Task<int> ExpireOverdueAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var overdue = await context.UserInvitations
            .IgnoreQueryFilters()
            .Where(invitation => invitation.ExpiresAtUtc <= asOf)
            .Where(invitation => invitation.Status == InvitationStatus.Pending
                                 || invitation.Status == InvitationStatus.Resent)
            .ToListAsync(cancellationToken);

        foreach (var invitation in overdue)
        {
            invitation.Status = InvitationStatus.Expired;
        }

        return overdue.Count;
    }
}

/// <summary>The three navigation tables.</summary>
public sealed class MenuRepository(IamDbContext context) : IMenuRepository
{
    /// <summary>
    /// The whole global catalogue. Small, read constantly, and not Tenant-owned, so it is
    /// fetched untracked in one query rather than walked node by node.
    /// </summary>
    public async Task<IReadOnlyList<MenuDefinition>> GetCatalogueAsync(CancellationToken cancellationToken) =>
        await context.MenuDefinitions
            .AsNoTracking()
            .OrderBy(menu => menu.Level)
            .ThenBy(menu => menu.DisplayOrder)
            .ToListAsync(cancellationToken);

    public Task<MenuDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken) =>
        context.MenuDefinitions.FirstOrDefaultAsync(menu => menu.Id == id, cancellationToken);

    public Task<MenuDefinition?> GetDefinitionByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.MenuDefinitions.FirstOrDefaultAsync(
            menu => menu.Code == code.ToUpperInvariant(), cancellationToken);

    public Task<bool> DefinitionCodeExistsAsync(
        string code, Guid? excludingId, CancellationToken cancellationToken) =>
        context.MenuDefinitions
            .Where(menu => excludingId == null || menu.Id != excludingId)
            .AnyAsync(menu => menu.Code == code.ToUpperInvariant(), cancellationToken);

    public async Task AddDefinitionAsync(MenuDefinition definition, CancellationToken cancellationToken) =>
        await context.MenuDefinitions.AddAsync(definition, cancellationToken);

    public void RemoveDefinition(MenuDefinition definition) => context.MenuDefinitions.Remove(definition);

    public async Task<IReadOnlyList<TenantMenu>> GetTenantMenusAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantMenus
            .IgnoreQueryFilters()
            .Include(item => item.MenuDefinition)
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public Task<TenantMenu?> GetTenantMenuAsync(
        Guid tenantId, Guid menuDefinitionId, CancellationToken cancellationToken) =>
        context.TenantMenus
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.MenuDefinitionId == menuDefinitionId,
                cancellationToken);

    public async Task AddTenantMenuAsync(TenantMenu tenantMenu, CancellationToken cancellationToken) =>
        await context.TenantMenus.AddAsync(tenantMenu, cancellationToken);

    public async Task<IReadOnlyList<RoleMenu>> GetRoleMenusAsync(
        Guid roleId, CancellationToken cancellationToken) =>
        await context.RoleMenus
            .Include(item => item.MenuDefinition)
            .Where(item => item.RoleId == roleId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Mappings for several roles at once.
    ///
    /// One query rather than one per role, because building a caller navigation needs the
    /// union across every role they hold and a loop would be a query per role per page load.
    /// </summary>
    public async Task<IReadOnlyList<RoleMenu>> GetRoleMenusForRolesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await context.RoleMenus
            .Include(item => item.MenuDefinition)
            .Where(item => roleIds.Contains(item.RoleId))
            .ToListAsync(cancellationToken);
    }

    public async Task AddRoleMenuAsync(RoleMenu roleMenu, CancellationToken cancellationToken) =>
        await context.RoleMenus.AddAsync(roleMenu, cancellationToken);

    public void RemoveRoleMenus(IEnumerable<RoleMenu> roleMenus) => context.RoleMenus.RemoveRange(roleMenus);
}

/// <summary>Departments and organisation units.</summary>
public sealed class OrganisationStructureRepository(IamDbContext context) : IOrganisationStructureRepository
{
    public Task<Department?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken) =>
        context.Departments.FirstOrDefaultAsync(department => department.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.Departments
            .IgnoreQueryFilters()
            .Where(department => department.TenantId == tenantId)
            .OrderBy(department => department.DisplayOrder)
            .ThenBy(department => department.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> DepartmentCodeExistsAsync(
        string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken) =>
        context.Departments
            .IgnoreQueryFilters()
            .Where(department => department.TenantId == tenantId)
            .Where(department => excludingId == null || department.Id != excludingId)
            .AnyAsync(department => department.Code == code.ToUpperInvariant(), cancellationToken);

    public async Task AddDepartmentAsync(Department department, CancellationToken cancellationToken) =>
        await context.Departments.AddAsync(department, cancellationToken);

    public void RemoveDepartment(Department department) => context.Departments.Remove(department);

    public Task<int> CountDepartmentMembersAsync(Guid departmentId, CancellationToken cancellationToken) =>
        context.Users.CountAsync(user => user.DepartmentId == departmentId, cancellationToken);

    public Task<OrganisationUnit?> GetUnitAsync(Guid id, CancellationToken cancellationToken) =>
        context.OrganisationUnits.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OrganisationUnit>> GetUnitsAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.OrganisationUnits
            .IgnoreQueryFilters()
            .Where(unit => unit.TenantId == tenantId)
            .OrderBy(unit => unit.DisplayOrder)
            .ThenBy(unit => unit.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> UnitCodeExistsAsync(
        string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken) =>
        context.OrganisationUnits
            .IgnoreQueryFilters()
            .Where(unit => unit.TenantId == tenantId)
            .Where(unit => excludingId == null || unit.Id != excludingId)
            .AnyAsync(unit => unit.Code == code.ToUpperInvariant(), cancellationToken);

    public async Task AddUnitAsync(OrganisationUnit unit, CancellationToken cancellationToken) =>
        await context.OrganisationUnits.AddAsync(unit, cancellationToken);

    public void RemoveUnit(OrganisationUnit unit) => context.OrganisationUnits.Remove(unit);

    public Task<int> CountUnitMembersAsync(Guid unitId, CancellationToken cancellationToken) =>
        context.Users.CountAsync(user => user.OrganisationUnitId == unitId, cancellationToken);
}

/// <summary>Access requests, reviews, identifier changes, data scopes and user claims.</summary>
public sealed class GovernanceRepository(IamDbContext context) : IGovernanceRepository
{
    public Task<AccessRequest?> GetAccessRequestAsync(Guid id, CancellationToken cancellationToken) =>
        context.AccessRequests
            .Include(request => request.RequestedForUser)
            .Include(request => request.Role)
            .FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<string> NextRequestNumberAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextNumberAsync(
            context.AccessRequests.IgnoreQueryFilters().Where(request => request.TenantId == tenantId),
            "AR", cancellationToken);

    public async Task AddAccessRequestAsync(AccessRequest request, CancellationToken cancellationToken) =>
        await context.AccessRequests.AddAsync(request, cancellationToken);

    /// <summary>Lapses requests nobody acted on, so the queue does not fill with stale rows.</summary>
    public async Task<int> ExpireOverdueRequestsAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var overdue = await context.AccessRequests
            .IgnoreQueryFilters()
            .Where(request => request.Status == AccessRequestStatus.Submitted
                              && request.ExpiresAtUtc != null
                              && request.ExpiresAtUtc <= asOf)
            .ToListAsync(cancellationToken);

        foreach (var request in overdue)
        {
            request.Status = AccessRequestStatus.Expired;
        }

        return overdue.Count;
    }

    public Task<AccessReview?> GetAccessReviewAsync(Guid id, CancellationToken cancellationToken) =>
        context.AccessReviews
            .Include(review => review.SubjectUser)
            .Include(review => review.Role)
            .Include(review => review.Campaign)
            .FirstOrDefaultAsync(review => review.Id == id, cancellationToken);

    public Task<string> NextReviewNumberAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextNumberAsync(
            context.AccessReviews.IgnoreQueryFilters().Where(review => review.TenantId == tenantId),
            "REV", cancellationToken);

    public async Task AddAccessReviewAsync(AccessReview review, CancellationToken cancellationToken) =>
        await context.AccessReviews.AddAsync(review, cancellationToken);

    public async Task<IReadOnlyList<AccessReview>> GetReviewsForCampaignAsync(
        Guid campaignId, CancellationToken cancellationToken) =>
        await context.AccessReviews
            .Include(review => review.SubjectUser)
            .Where(review => review.CampaignId == campaignId)
            .ToListAsync(cancellationToken);

    public Task<AccessReviewCampaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken) =>
        context.AccessReviewCampaigns.FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public Task<bool> CampaignCodeExistsAsync(
        string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken) =>
        context.AccessReviewCampaigns
            .IgnoreQueryFilters()
            .Where(campaign => campaign.TenantId == tenantId)
            .Where(campaign => excludingId == null || campaign.Id != excludingId)
            .AnyAsync(campaign => campaign.Code == code.ToUpperInvariant(), cancellationToken);

    public async Task AddCampaignAsync(AccessReviewCampaign campaign, CancellationToken cancellationToken) =>
        await context.AccessReviewCampaigns.AddAsync(campaign, cancellationToken);

    public async Task<int> MarkOverdueReviewsAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var overdue = await context.AccessReviews
            .IgnoreQueryFilters()
            .Where(review => review.ReviewDueAtUtc < asOf)
            .Where(review => review.Status == AccessReviewStatus.Open
                             || review.Status == AccessReviewStatus.InProgress)
            .ToListAsync(cancellationToken);

        foreach (var review in overdue)
        {
            review.Status = AccessReviewStatus.Overdue;
        }

        return overdue.Count;
    }

    public Task<LoginIdentifierChangeRequest?> GetIdentifierChangeAsync(
        Guid id, CancellationToken cancellationToken) =>
        context.LoginIdentifierChangeRequests
            .Include(request => request.User)
            .FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<LoginIdentifierChangeRequest?> GetOpenIdentifierChangeAsync(
        Guid userId, CancellationToken cancellationToken) =>
        context.LoginIdentifierChangeRequests
            .Where(request => request.UserId == userId)
            .Where(request => request.Status == LoginIdentifierChangeStatus.Draft
                              || request.Status == LoginIdentifierChangeStatus.PendingVerification
                              || request.Status == LoginIdentifierChangeStatus.PendingApproval
                              || request.Status == LoginIdentifierChangeStatus.Approved)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddIdentifierChangeAsync(
        LoginIdentifierChangeRequest request, CancellationToken cancellationToken) =>
        await context.LoginIdentifierChangeRequests.AddAsync(request, cancellationToken);

    public async Task<IReadOnlyList<UserDataScope>> GetDataScopesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.UserDataScopes
            .Where(scope => scope.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task<UserDataScope?> GetDataScopeAsync(Guid id, CancellationToken cancellationToken) =>
        context.UserDataScopes.FirstOrDefaultAsync(scope => scope.Id == id, cancellationToken);

    public async Task AddDataScopeAsync(UserDataScope scope, CancellationToken cancellationToken) =>
        await context.UserDataScopes.AddAsync(scope, cancellationToken);

    public async Task<IReadOnlyList<UserClaimEntry>> GetUserClaimsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.UserClaims
            .Where(claim => claim.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task AddUserClaimAsync(UserClaimEntry claim, CancellationToken cancellationToken) =>
        await context.UserClaims.AddAsync(claim, cancellationToken);

    public void RemoveUserClaims(IEnumerable<UserClaimEntry> claims) =>
        context.UserClaims.RemoveRange(claims);

    /// <summary>
    /// A sequential reference such as AR-2026-00128.
    ///
    /// The year is part of the number, so the sequence restarts annually and a reference is
    /// recognisable at a glance. Counted per Organisation, so two Organisations never share
    /// one.
    /// </summary>
    private static async Task<string> NextNumberAsync<TEntity>(
        IQueryable<TEntity> scope, string prefix, CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var existing = await scope.CountAsync(cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{year}-{existing + 1:D5}");
    }
}

/// <summary>Bulk user administration jobs.</summary>
public sealed class BulkOperationRepository(IamDbContext context) : IBulkOperationRepository
{
    public Task<BulkOperation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.BulkOperations.FirstOrDefaultAsync(operation => operation.Id == id, cancellationToken);

    public Task<BulkOperation?> GetWithItemsAsync(Guid id, CancellationToken cancellationToken) =>
        context.BulkOperations
            .Include(operation => operation.Items.OrderBy(item => item.RowNumber))
            .FirstOrDefaultAsync(operation => operation.Id == id, cancellationToken);

    public async Task<string> NextOperationNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;

        var existing = await context.BulkOperations
            .IgnoreQueryFilters()
            .CountAsync(operation => operation.TenantId == tenantId, cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"BLK-{year}-{existing + 1:D5}");
    }

    public async Task AddAsync(BulkOperation operation, CancellationToken cancellationToken) =>
        await context.BulkOperations.AddAsync(operation, cancellationToken);

    public async Task AddItemsAsync(
        IEnumerable<BulkOperationItem> items, CancellationToken cancellationToken) =>
        await context.BulkOperationItems.AddRangeAsync(items, cancellationToken);

    public async Task<IReadOnlyList<BulkOperationItem>> GetItemsAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        await context.BulkOperationItems
            .Where(item => item.BulkOperationId == operationId)
            .OrderBy(item => item.RowNumber)
            .ToListAsync(cancellationToken);
}

/// <summary>Audit writing, the outbox and idempotency records.</summary>
public sealed class AuditRepository(IamDbContext context) : IAuditRepository
{
    public async Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        await context.AuditEvents.AddAsync(auditEvent, cancellationToken);

    public async Task AddOutboxAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        await context.OutboxMessages.AddAsync(message, cancellationToken);

    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(
        string key, string endpoint, Guid? tenantId, CancellationToken cancellationToken) =>
        context.IdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                record => record.IdempotencyKey == key
                          && record.Endpoint == endpoint
                          && record.TenantId == tenantId,
                cancellationToken);

    public async Task AddIdempotencyRecordAsync(
        IdempotencyRecord record, CancellationToken cancellationToken) =>
        await context.IdempotencyRecords.AddAsync(record, cancellationToken);
}

/// <summary>
/// Dropdown data.
///
/// Everything here is a projection straight into <see cref="LookupItem"/> with no tracking:
/// loading whole aggregates to render a select box is how a screen ends up issuing forty
/// queries.
/// </summary>
public sealed class LookupRepository(IamDbContext context, ITenantContext tenantContext) : ILookupRepository
{
    /// <summary>
    /// The assignable roles for a form.
    ///
    /// PLATFORM roles are excluded. The Role query filter keeps null-tenant rows visible so a
    /// SuperAdmin can load their own grant while inside an Organisation, which would otherwise
    /// put SUPER_ADMIN in this dropdown — a role no member of an Organisation can be given.
    /// </summary>
    public async Task<IReadOnlyList<LookupItem>> GetRolesAsync(CancellationToken cancellationToken) =>
        await context.Roles
            .AsNoTracking()
            .Where(role => role.TenantId != null && role.Status == RoleStatus.Active)
            .OrderByDescending(role => role.Priority)
            .ThenBy(role => role.Name)
            .Select(role => new LookupItem(role.Id, role.Code, role.Name ?? role.Code, true, role.Description))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> GetDepartmentsAsync(CancellationToken cancellationToken) =>
        await context.Departments
            .AsNoTracking()
            .Where(department => department.Status == RecordStatus.Active)
            .OrderBy(department => department.DisplayOrder)
            .ThenBy(department => department.Name)
            .Select(department => new LookupItem(
                department.Id, department.Code, department.Name, true, department.Description))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> GetOrganisationUnitsAsync(CancellationToken cancellationToken) =>
        await context.OrganisationUnits
            .AsNoTracking()
            .Where(unit => unit.Status == RecordStatus.Active)
            .OrderBy(unit => unit.DisplayOrder)
            .ThenBy(unit => unit.Name)
            .Select(unit => new LookupItem(unit.Id, unit.Code, unit.Name, true, unit.UnitType))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Candidate managers. Only Active users, because assigning somebody to report to a
    /// deactivated account produces a broken hierarchy nobody notices until later.
    /// </summary>
    public async Task<IReadOnlyList<LookupItem>> GetManagersAsync(CancellationToken cancellationToken) =>
        await context.Users
            .AsNoTracking()
            .Where(user => user.Status == UserStatus.Active)
            .OrderBy(user => user.DisplayName)
            .Select(user => new LookupItem(user.Id, user.Code, user.DisplayName, true, user.Designation))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await context.Permissions
            .AsNoTracking()
            .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
            .OrderBy(permission => permission.ModuleCode)
            .ThenBy(permission => permission.DisplayOrder)
            .Select(permission => new LookupItem(
                permission.Id, permission.Code, permission.Name, true, permission.Description))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The Organisation switcher.
    ///
    /// Returns EMPTY for a Tenant user rather than refusing: the switcher is simply not part
    /// of their interface, and a 403 would imply there is something there to reach.
    /// </summary>
    public async Task<IReadOnlyList<LookupItem>> GetSelectableTenantsAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return [];
        }

        return await context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Status != TenantStatus.Archived)
            .OrderBy(tenant => tenant.Name)
            .Select(tenant => new LookupItem(
                tenant.Id, tenant.Code, tenant.Name,
                tenant.Status == TenantStatus.Active,
                tenant.Subdomain))
            .ToListAsync(cancellationToken);
    }
}
