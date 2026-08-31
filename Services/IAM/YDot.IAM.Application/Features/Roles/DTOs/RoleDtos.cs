using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Roles.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Creating a role inside the caller Organisation.
///
/// No TenantId field, for the same reason as everywhere else: the Organisation comes from the
/// token. A role created here is genuinely separate from a role of the same name in another
/// Organisation.
/// </summary>
public sealed record CreateRoleRequest(
    string Name,
    string? Code = null,
    string? Description = null,
    RoleStatus Status = RoleStatus.Draft,
    int Priority = 0,
    bool IsPrivileged = false,
    bool IsDefaultRole = false,
    string? DisplayTag = null,

    /// <summary>Permission codes attached on creation. Platform-only codes are rejected.</summary>
    IReadOnlyList<string>? PermissionCodes = null,

    /// <summary>Menu nodes this role may see. Empty means no navigation restriction.</summary>
    IReadOnlyList<Guid>? VisibleMenuIds = null);

/// <summary>Editing a role.</summary>
public sealed record UpdateRoleRequest(
    long ExpectedVersion,
    string? Name = null,
    string? Description = null,
    int? Priority = null,
    bool? IsPrivileged = null,
    bool? IsDefaultRole = null,
    string? DisplayTag = null);

/// <summary>
/// Replacing a role permission set.
///
/// The WHOLE set is sent, not a delta, so the outcome does not depend on what the screen
/// happened to be showing when somebody opened it.
/// </summary>
public sealed record AssignRolePermissionsRequest(
    IReadOnlyList<string> PermissionCodes,
    long ExpectedVersion,

    /// <summary>Codes explicitly denied. Deny beats allow, so one permission can be carved out
    /// of a broad role without unpicking the role.</summary>
    IReadOnlyList<string>? DeniedPermissionCodes = null,

    string? Justification = null);

/// <summary>Activating or deactivating a role.</summary>
public sealed record ChangeRoleStatusRequest(RoleStatus Status, long ExpectedVersion, string? Reason = null);

/// <summary>Deleting a role. Refused when anybody holds it.</summary>
public sealed record DeleteRoleRequest(long ExpectedVersion, string? Reason = null);

/// <summary>Declaring that two roles may not be held by the same person.</summary>
public sealed record CreateRoleIncompatibilityRequest(
    Guid RoleId,
    Guid ConflictingRoleId,
    string Reason,
    bool IsBlocking = true);

/// <summary>Adding a claim carried by every holder of a role.</summary>
public sealed record AssignRoleClaimsRequest(
    IReadOnlyList<RoleClaimRequest> Claims,
    long ExpectedVersion);

/// <summary>One role claim.</summary>
public sealed record RoleClaimRequest(string ClaimType, string ClaimValue, string? Description = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the role catalogue.</summary>
public sealed record RoleListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    RoleType RoleType,
    RoleStatus Status,
    string StatusDisplay,
    bool IsSystemRole,
    bool IsDefaultRole,
    bool IsPrivileged,
    bool GrantsAllTenantPermissions,
    int Priority,
    string? DisplayTag,
    int PermissionCount,
    int MemberCount,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>A role with its permissions, members, claims and conflicts.</summary>
public sealed record RoleDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string Code,
    string Name,
    string? Description,
    RoleType RoleType,
    RoleStatus Status,
    string StatusDisplay,
    bool IsSystemRole,
    bool IsDefaultRole,
    bool IsPrivileged,
    bool GrantsAllTenantPermissions,
    int Priority,
    string? DisplayTag,
    int MemberCount,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<RolePermissionResponse> Permissions,
    IReadOnlyList<RoleClaimResponse> Claims,
    IReadOnlyList<RoleIncompatibilityResponse> Incompatibilities,
    IReadOnlyList<Guid> VisibleMenuIds,
    IReadOnlyList<string> PermittedActions);

/// <summary>One permission attached to a role.</summary>
public sealed record RolePermissionResponse(
    Guid Id,
    Guid PermissionId,
    string PermissionCode,
    string PermissionName,
    string ModuleCode,
    string? GroupCode,
    PermissionAction Action,
    bool IsSensitive,
    bool IsDenied,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>One claim carried by holders of a role.</summary>
public sealed record RoleClaimResponse(int Id, string ClaimType, string ClaimValue, string? Description);

/// <summary>One segregation-of-duties rule.</summary>
public sealed record RoleIncompatibilityResponse(
    Guid Id,
    Guid RoleId,
    string RoleName,
    Guid ConflictingRoleId,
    string ConflictingRoleName,
    string Reason,
    bool IsBlocking,
    bool IsActive);

/// <summary>One option in a role picker.</summary>
public sealed record RoleLookupResponse(
    Guid Id,
    string Code,
    string Name,
    RoleStatus Status,
    bool IsPrivileged,
    bool IsDefaultRole,
    int PermissionCount);

/// <summary>Who holds a role.</summary>
public sealed record RoleMemberResponse(
    Guid UserRoleId,
    Guid UserId,
    string UserCode,
    string DisplayName,
    string Email,
    UserStatus UserStatus,
    UserRoleAssignmentStatus AssignmentStatus,
    bool IsPrimary,
    bool IsEffective,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? EffectiveToUtc);

/// <summary>One row of the permission catalogue.</summary>
public sealed record PermissionListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string ModuleCode,
    string? GroupCode,
    PermissionAction Action,
    bool IsSensitive,
    bool IsPlatformOnly,
    PermissionStatus Status,
    int DisplayOrder);

/// <summary>
/// The permission matrix the role editor renders.
///
/// Grouped by module and then by group, because a flat list of a hundred and thirty codes is
/// unusable — and the whole point of the screen is to let somebody reason about what a role
/// can do.
/// </summary>
public sealed record PermissionMatrixResponse(
    Guid? RoleId,
    string? RoleName,
    bool GrantsAllTenantPermissions,
    IReadOnlyList<PermissionModuleResponse> Modules,
    int TotalPermissionCount,
    int GrantedCount,
    int SensitiveGrantedCount);

/// <summary>One module block of the matrix.</summary>
public sealed record PermissionModuleResponse(
    string ModuleCode,
    string ModuleName,
    IReadOnlyList<PermissionMatrixGroupResponse> Groups,
    int GrantedCount,
    int TotalCount);

/// <summary>One group inside a module.</summary>
public sealed record PermissionMatrixGroupResponse(
    string GroupCode,
    string GroupName,
    IReadOnlyList<PermissionMatrixItemResponse> Permissions);

/// <summary>One cell of the matrix.</summary>
public sealed record PermissionMatrixItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    PermissionAction Action,
    bool IsSensitive,
    bool IsGranted,
    bool IsDenied,

    /// <summary>
    /// True when the role carries this only because it grants everything. The checkbox is
    /// shown ticked and disabled, so nobody tries to untick something that is not a row.
    /// </summary>
    bool IsImplicitlyGranted);
