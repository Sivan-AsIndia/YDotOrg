using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Audit.DTOs;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read-side projections for the grids.
///
/// WHY THESE ARE SEPARATE FROM THE REPOSITORIES. A repository loads a tracked aggregate so it
/// can be changed. A grid wants twelve columns from four tables for twenty rows, and loading
/// twenty full aggregates with their roles, sessions and claims to render a list is how a
/// screen ends up issuing forty queries and holding a change-tracker full of entities nobody
/// intends to modify.
///
/// Everything here projects straight into the response DTO with no tracking. The
/// <see cref="AccessScope"/> argument carries the caller data scope into the query, so no
/// read service has to reach for HttpContext — and the Organisation filter is already applied
/// underneath by the DbContext, so scope here only ever NARROWS within one Organisation.
/// </summary>
public interface IUserReadService
{
    Task<PagedResponse<UserListItemResponse>> SearchAsync(
        UserSearchFilter filter, AccessScope scope, CancellationToken cancellationToken);

    Task<UserDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveContact, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserLookupResponse>> LookupAsync(
        string? search, int take, CancellationToken cancellationToken);

    /// <summary>Everything IAM-USR-04 shows: sessions, devices, MFA methods and recent attempts.</summary>
    Task<UserSecurityResponse?> GetSecurityAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The IAM-USR-03 preview: roles, permissions and data scopes resolved together.</summary>
    Task<UserAccessPreviewResponse?> GetAccessPreviewAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Counts by status, for the directory summary tiles.</summary>
    Task<UserStatisticsResponse> GetStatisticsAsync(AccessScope scope, CancellationToken cancellationToken);

    /// <summary>The rows behind a CSV export, already scoped and masked.</summary>
    Task<IReadOnlyList<UserExportRow>> GetExportRowsAsync(
        UserSearchFilter filter, AccessScope scope, bool canSeeSensitiveContact, CancellationToken cancellationToken);
}

/// <summary>Read side for the Organisation directory and detail screens.</summary>
public interface IOrganisationReadService
{
    /// <summary>
    /// The Organisation directory. Platform-scope only — reaching the endpoint that calls this
    /// requires <c>platform.organisations.view</c>, and the query deliberately ignores the
    /// ambient Organisation filter because listing every Organisation is the whole point.
    /// </summary>
    Task<PagedResponse<OrganisationListItemResponse>> SearchAsync(
        TenantSearchFilter filter, CancellationToken cancellationToken);

    Task<OrganisationDetailResponse?> GetDetailAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The caller own Organisation, for the TenantAdmin profile screen. Resolved from the
    /// request context rather than an id, so a Tenant user cannot ask for a different one.
    /// </summary>
    Task<OrganisationDetailResponse?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<OrganisationStatisticsResponse> GetStatisticsAsync(
        Guid businessUnitId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganisationListItemResponse>> GetAwaitingReviewAsync(
        Guid businessUnitId, CancellationToken cancellationToken);
}

/// <summary>Read side for the role and permission catalogue.</summary>
public interface IRoleReadService
{
    Task<PagedResponse<RoleListItemResponse>> SearchAsync(
        RoleSearchFilter filter, CancellationToken cancellationToken);

    Task<RoleDetailResponse?> GetDetailAsync(Guid roleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleLookupResponse>> LookupAsync(CancellationToken cancellationToken);

    /// <summary>The permission matrix: every assignable permission, grouped, flagged by role.</summary>
    Task<PermissionMatrixResponse> GetPermissionMatrixAsync(
        Guid? roleId, CancellationToken cancellationToken);

    Task<PagedResponse<PermissionListItemResponse>> SearchPermissionsAsync(
        PermissionSearchFilter filter, CancellationToken cancellationToken);

    /// <summary>Who holds this role, for the role detail screen.</summary>
    Task<PagedResponse<RoleMemberResponse>> GetMembersAsync(
        Guid roleId, PaginationRequest pagination, CancellationToken cancellationToken);
}

/// <summary>Read side for the audit trail.</summary>
public interface IAuditReadService
{
    Task<PagedResponse<AuditEventResponse>> SearchAsync(
        AuditEventSearchFilter filter, bool canSeeSensitive, CancellationToken cancellationToken);

    Task<AuditEventResponse?> GetAsync(Guid id, bool canSeeSensitive, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEventResponse>> GetForTargetAsync(
        string targetType, Guid targetId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// The record types this Organisation's trail actually contains, for the filter dropdown.
    ///
    /// READ FROM THE DATA, not from a list of the entity names somebody remembered. The screen
    /// carried eleven hardcoded types; a type the platform started writing after that list was
    /// typed could never be filtered for, and a type that had never occurred was offered as a
    /// filter that returns nothing. Both failures are silent.
    /// </summary>
    Task<IReadOnlyList<string>> GetTargetTypesAsync(CancellationToken cancellationToken);
}

/// <summary>Read side for access requests, reviews and identifier changes.</summary>
public interface IGovernanceReadService
{
    Task<PagedResponse<AccessRequestListItemResponse>> SearchRequestsAsync(
        AccessRequestSearchFilter filter, Guid currentUserId, CancellationToken cancellationToken);

    Task<AccessRequestDetailResponse?> GetRequestAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResponse<AccessReviewListItemResponse>> SearchReviewsAsync(
        AccessReviewSearchFilter filter, Guid currentUserId, CancellationToken cancellationToken);

    Task<AccessReviewDetailResponse?> GetReviewAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessReviewCampaignResponse>> GetCampaignsAsync(CancellationToken cancellationToken);

    Task<AccessReviewCampaignResponse?> GetCampaignAsync(Guid id, CancellationToken cancellationToken);

    Task<LoginIdentifierChangeResponse?> GetIdentifierChangeAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoginIdentifierChangeResponse>> GetIdentifierChangesForUserAsync(
        Guid userId, CancellationToken cancellationToken);
}

/// <summary>Read side for bulk user administration.</summary>
public interface IBulkOperationReadService
{
    Task<PagedResponse<BulkOperationListItemResponse>> SearchAsync(
        PaginationRequest pagination, CancellationToken cancellationToken);

    Task<BulkOperationDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}
