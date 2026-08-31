using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Mappings;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read-side projections for the Users screens.
///
/// EVERY QUERY IS ALREADY ORGANISATION-SCOPED before it reaches here: the DbContext query
/// filter sees to that, so none of these methods has to remember a Where clause and none of
/// them can forget one.
///
/// What this class adds is the two things a filter cannot express — the caller narrowing data
/// scope, and whether contact details leave the server in the clear. Both are applied in the
/// PROJECTION rather than afterwards, so an unmasked value is never materialised in a place
/// it could be logged or serialised by mistake.
/// </summary>
public sealed class UserReadService(
    IamDbContext context,
    IEffectiveAccessService effectiveAccess,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IUserReadService
{
    public async Task<PagedResponse<UserListItemResponse>> SearchAsync(
        UserSearchFilter filter, AccessScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = clock.UtcNow;
        var query = ExcludePlatformAccounts(ApplyScope(context.Users.AsNoTracking(), scope));

        query = ApplyFilters(query, filter, now);

        var total = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(user => new
            {
                user.Id,
                user.Code,
                user.DisplayName,
                user.Email,
                user.UserName,
                user.Status,
                user.AccountCategory,
                DepartmentName = user.Department != null ? user.Department.Name : null,
                OrganisationUnitName = user.OrganisationUnit != null ? user.OrganisationUnit.Name : null,
                user.Designation,
                user.TwoFactorEnabled,
                user.LockoutEnd,
                user.IsLockedOutByAdministrator,
                user.LastLoginAtUtc,
                user.AccessEndsAtUtc,
                user.AvatarUrl,
                user.UpdatedAtUtc,
                user.CreatedAtUtc,
                user.Version,
                RoleNames = user.UserRoles
                    .Where(assignment => assignment.Status == UserRoleAssignmentStatus.Active)
                    .Select(assignment => assignment.Role!.Name ?? assignment.Role.Code)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new UserListItemResponse(
                row.Id,
                row.Code,
                row.DisplayName,
                row.Email ?? string.Empty,
                row.UserName ?? string.Empty,
                row.Status,
                UserMappingConfig.DescribeStatus(row.Status),
                row.AccountCategory,
                row.DepartmentName,
                row.OrganisationUnitName,
                row.Designation,
                row.RoleNames,
                row.TwoFactorEnabled,
                row.IsLockedOutByAdministrator || (row.LockoutEnd.HasValue && row.LockoutEnd.Value > now),
                row.LastLoginAtUtc,
                row.AccessEndsAtUtc,
                row.AvatarUrl,
                row.UpdatedAtUtc ?? row.CreatedAtUtc,
                row.Version))
            .ToList();

        return new PagedResponse<UserListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<UserDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveContact, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await ApplyScope(context.Users.AsNoTracking(), scope)
            .Include(item => item.Department)
            .Include(item => item.OrganisationUnit)
            .Include(item => item.Manager)
            .Include(item => item.UserRoles).ThenInclude(assignment => assignment.Role)
            .Include(item => item.DataScopes)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var invitation = await context.UserInvitations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.UserId == id)
            .Where(item => item.Status == InvitationStatus.Pending || item.Status == InvitationStatus.Resent)
            .OrderByDescending(item => item.InvitedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var permissionCountByRole = await context.RolePermissions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(grant => user.UserRoles.Select(assignment => assignment.RoleId).Contains(grant.RoleId))
            .Where(grant => !grant.IsDenied)
            .GroupBy(grant => grant.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.RoleId, item => item.Count, cancellationToken);

        var hasMobile = !string.IsNullOrWhiteSpace(user.MobileNumber);

        return new UserDetailResponse(
            user.Id,
            user.TenantId,
            user.BusinessUnitId,
            user.Code,
            user.EmployeeNumber,
            user.FirstName,
            user.MiddleName,
            user.LastName,
            user.DisplayName,
            // Masked in the projection, so the clear value never reaches a caller who is not
            // entitled to it.
            UserMappingConfig.MaskEmail(user.Email, canSeeSensitiveContact),
            user.EmailConfirmed,
            user.EmailConfirmedAtUtc,
            user.UserName ?? string.Empty,
            user.MobileCountryCode,
            UserMappingConfig.MaskMobile(user.MobileNumber, canSeeSensitiveContact),
            user.MobileConfirmed,
            user.AccountCategory,
            user.EngagementType,
            user.DepartmentId,
            user.Department?.Name,
            user.OrganisationUnitId,
            user.OrganisationUnit?.Name,
            user.Designation,
            user.ManagerUserId,
            user.Manager?.DisplayName,
            user.Status,
            UserMappingConfig.DescribeStatus(user.Status),
            user.AccessStartsAtUtc,
            user.AccessEndsAtUtc,
            user.MfaRequirement,
            user.MfaEnabled,
            user.PrivilegeLevel,
            user.IsSuperAdmin,
            user.IsTenantAdmin,
            user.IsSystemAccount,
            user.MustChangePassword,
            user.IsLockedOut(now),
            user.LockoutEnd,
            user.LockoutReason,
            user.AccessFailedCount,
            user.LastLoginAtUtc,
            user.LastLoginIpAddress,
            user.LastLoginClientType,
            user.LastLoginBrowser,
            user.LastLoginOperatingSystem,
            user.JoinedOn,
            user.ExitedOn,
            user.PreferredCulture,
            user.TimeZone,
            user.AvatarUrl,
            user.CreatedAtUtc,
            user.CreatedByUserId,
            user.UpdatedAtUtc,
            user.UpdatedByUserId,
            user.Version,
            [
                .. user.UserRoles
                    .OrderByDescending(assignment => assignment.IsPrimary)
                    .Select(assignment => new UserRoleAssignmentResponse(
                        assignment.Id,
                        assignment.RoleId,
                        assignment.Role?.Code ?? string.Empty,
                        assignment.Role?.Name ?? assignment.Role?.Code ?? string.Empty,
                        assignment.Status,
                        assignment.IsPrimary,
                        assignment.IsEffective(now),
                        assignment.AssignedAtUtc,
                        null,
                        assignment.EffectiveFromUtc,
                        assignment.EffectiveToUtc,
                        assignment.Justification,
                        permissionCountByRole.GetValueOrDefault(assignment.RoleId)))
            ],
            [
                .. user.DataScopes.Select(item => new UserDataScopeResponse(
                    item.Id, item.ScopeType, item.ScopeValue, item.DisplayLabel,
                    item.IsEffective(now), item.GrantedAtUtc, item.EffectiveFromUtc, item.EffectiveToUtc))
            ],
            // Told explicitly, so the screen can offer a reveal rather than showing asterisks
            // as though they were the value.
            EmailMasked: !canSeeSensitiveContact && !string.IsNullOrWhiteSpace(user.Email),
            MobileMasked: !canSeeSensitiveContact && hasMobile,
            HasPendingInvitation: invitation is not null,
            InvitationExpiresAtUtc: invitation?.ExpiresAtUtc,
            UserMappingConfig.PermittedActionsFor(user, now));
    }

    public async Task<IReadOnlyList<UserLookupResponse>> LookupAsync(
        string? search, int take, CancellationToken cancellationToken)
    {
        var query = ExcludePlatformAccounts(context.Users.AsNoTracking())
            .Where(user => user.Status == UserStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(user =>
                user.DisplayName.ToLower().Contains(term)
                || user.Code.ToLower().Contains(term)
                || (user.Email != null && user.Email.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(user => user.DisplayName)
            .Take(take)
            .Select(user => new UserLookupResponse(
                user.Id, user.Code, user.DisplayName, user.Email ?? string.Empty, user.Status))
            .ToListAsync(cancellationToken);
    }

    /// <summary>IAM-USR-04: sessions, devices, MFA methods and recent attempts.</summary>
    public async Task<UserSecurityResponse?> GetSecurityAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // THE TENANT CHECK HAPPENS HERE, on this query, which is filtered. A user in another
        // Organisation simply is not found, and the caller gets nothing back.
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        // WHY THE COLLECTIONS BELOW LIFT THE FILTER.
        //
        // Sessions, devices, factors and sign-in attempts all belong to the user resolved
        // above, and that lookup was filtered — so the authorization has already happened and
        // `UserId == userId` is a tighter boundary than the tenant filter, not a looser one.
        //
        // Leaving the filter on was actively wrong. A SuperAdmin's own rows carry the platform
        // sentinel rather than an Organisation id, so while they operated inside TEN001 their
        // OWN security page showed no sessions, no devices and no factors — and they could not
        // end a session on a lost phone without first leaving the Organisation. Any record
        // whose tenant differed from the operating context vanished the same way.

        var tenant = user.TenantId.HasValue
            ? await context.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == user.TenantId.Value, cancellationToken)
            : null;

        var maximumAttempts = tenant?.MaximumFailedAccessAttempts ?? 5;

        var sessions = await context.UserSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null
                              && session.ExpiresAtUtc > now)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .Select(session => new
            {
                session.Id,
                session.DeviceName,
                session.ClientType,
                session.Browser,
                session.OperatingSystem,
                session.IpAddress,
                session.Location,
                session.IssuedAtUtc,
                session.LastActivityAtUtc,
                session.ExpiresAtUtc,
                session.MfaCompleted,
                session.IsTrustedDevice,
                session.OperatingTenantId,
                OperatingTenantName = session.OperatingTenantId == null
                    ? null
                    : context.Tenants.Where(item => item.Id == session.OperatingTenantId)
                        .Select(item => item.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var devices = await context.TrustedDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(device => device.UserId == userId && device.RevokedAtUtc == null)
            .OrderByDescending(device => device.LastSeenAtUtc ?? device.TrustedAtUtc)
            .ToListAsync(cancellationToken);

        var methods = await context.MfaMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(method => method.UserId == userId && method.Status != MfaMethodStatus.Revoked)
            .OrderByDescending(method => method.IsPrimary)
            .ToListAsync(cancellationToken);

        var attempts = await context.SignInAttempts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.AttemptedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new UserSecurityResponse(
            user.Id,
            user.DisplayName,
            user.MfaEnabled,
            user.MfaRequirement,
            user.IsMfaRequired(tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional),
            user.MfaEnrolledAtUtc,
            user.RecoveryCodesRemaining,
            user.IsLockedOut(now),
            user.LockoutEnd,
            user.LockoutReason,
            user.AccessFailedCount,
            Math.Max(0, maximumAttempts - user.AccessFailedCount),
            user.PasswordChangedAtUtc,
            user.MustChangePassword,
            [
                .. sessions.Select(session => new UserSessionResponse(
                    session.Id, session.DeviceName, session.ClientType, session.Browser,
                    session.OperatingSystem, session.IpAddress, session.Location,
                    session.IssuedAtUtc, session.LastActivityAtUtc, session.ExpiresAtUtc,
                    session.MfaCompleted, session.IsTrustedDevice,

                    // WHICH ONE IS THIS ONE. The security page needs it so it can decline to
                    // offer a Revoke button on the session the person is reading the page in —
                    // pressing that would sign them out of the screen they are standing on.
                    //
                    // On the administrative view of somebody ELSE this is false throughout,
                    // and correctly so: none of their sessions is the caller's.
                    IsCurrent: currentUser.SessionId == session.Id,
                    session.OperatingTenantId, session.OperatingTenantName))
            ],
            [
                .. devices.Select(device => new TrustedDeviceResponse(
                    device.Id, device.DeviceName, device.ClientType, device.Browser,
                    device.OperatingSystem, device.IpAddress, device.Location,
                    device.TrustedAtUtc, device.ExpiresAtUtc, device.LastSeenAtUtc,
                    device.ExpiresAtUtc <= now))
            ],
            [
                .. methods.Select(method => new MfaMethodResponse(
                    method.Id, method.MethodType, method.Label, method.MaskedDestination,
                    method.IsPrimary, method.Status, method.VerifiedAtUtc, method.LastUsedAtUtc))
            ],
            [
                .. attempts.Select(attempt => new SignInAttemptResponse(
                    attempt.Id, attempt.Outcome, UserMappingConfig.DescribeOutcome(attempt.Outcome),
                    attempt.Succeeded, attempt.AttemptedAtUtc, attempt.IpAddress, attempt.ClientType,
                    attempt.Browser, attempt.OperatingSystem, attempt.Location,
                    attempt.AttemptsRemaining, attempt.TriggeredLockout))
            ]);
    }

    /// <summary>
    /// IAM-USR-03: everything the person can do, grouped so it is readable.
    ///
    /// <c>GrantedVia</c> is what makes the screen useful rather than merely accurate: it
    /// answers "why do they have this?" without an administrator opening every role in turn.
    /// </summary>
    public async Task<UserAccessPreviewResponse?> GetAccessPreviewAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await context.Users
            .AsNoTracking()
            .Include(item => item.UserRoles).ThenInclude(assignment => assignment.Role)
            .Include(item => item.DataScopes)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var access = await effectiveAccess.ResolveAsync(userId, user.TenantId, cancellationToken);

        var catalogue = await context.Permissions
            .AsNoTracking()
            .Where(permission => permission.Status == PermissionStatus.Active)
            .Where(permission => !permission.IsPlatformOnly || user.IsSuperAdmin)
            .ToListAsync(cancellationToken);

        var roleIds = user.UserRoles.Select(assignment => assignment.RoleId).ToList();

        // Which role granted each code, so the screen can explain itself.
        var grantSources = roleIds.Count == 0
            ? []
            : await context.RolePermissions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(grant => roleIds.Contains(grant.RoleId) && !grant.IsDenied)
                .Select(grant => new
                {
                    grant.PermissionCode,
                    RoleName = grant.Role!.Name ?? grant.Role.Code
                })
                .ToListAsync(cancellationToken);

        var sourceByCode = grantSources
            .GroupBy(grant => grant.PermissionCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(item => item.RoleName).Distinct()),
                StringComparer.Ordinal);

        var directClaimCodes = await context.UserClaims
            .AsNoTracking()
            .Where(claim => claim.UserId == userId
                            && claim.ClaimType == Application.Common.Constants.ClaimTypeNames.Permission)
            .Select(claim => claim.ClaimValue)
            .ToListAsync(cancellationToken);

        var groups = UserMappingConfig.GroupPermissions(
            catalogue.Select(permission =>
            {
                var granted = access.HasPermission(permission.Code);

                var via = !granted
                    ? null
                    : user.IsSuperAdmin
                        ? "Platform administrator"
                        : sourceByCode.TryGetValue(permission.Code, out var roleName)
                            ? roleName
                            : directClaimCodes.Contains(permission.Code, StringComparer.Ordinal)
                                ? "Direct grant"
                                : access.HasAllTenantPermissions
                                    ? "Organisation administrator"
                                    : null;

                return (permission.Code, permission.Name, permission.ModuleCode, permission.GroupCode,
                    permission.Action, permission.IsSensitive, granted, via);
            }));

        var permissionCountByRole = grantSources
            .GroupBy(grant => grant.RoleName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new UserAccessPreviewResponse(
            user.Id,
            user.DisplayName,
            user.IsSuperAdmin,
            access.HasAllTenantPermissions,
            [
                .. user.UserRoles
                    .OrderByDescending(assignment => assignment.IsPrimary)
                    .Select(assignment => new UserRoleAssignmentResponse(
                        assignment.Id,
                        assignment.RoleId,
                        assignment.Role?.Code ?? string.Empty,
                        assignment.Role?.Name ?? assignment.Role?.Code ?? string.Empty,
                        assignment.Status,
                        assignment.IsPrimary,
                        assignment.IsEffective(now),
                        assignment.AssignedAtUtc,
                        null,
                        assignment.EffectiveFromUtc,
                        assignment.EffectiveToUtc,
                        assignment.Justification,
                        permissionCountByRole.GetValueOrDefault(
                            assignment.Role?.Name ?? assignment.Role?.Code ?? string.Empty)))
            ],
            groups,
            [
                .. user.DataScopes.Select(item => new UserDataScopeResponse(
                    item.Id, item.ScopeType, item.ScopeValue, item.DisplayLabel,
                    item.IsEffective(now), item.GrantedAtUtc, item.EffectiveFromUtc, item.EffectiveToUtc))
            ],
            [.. directClaimCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!)],
            access.PermissionCodes.Count,
            access.PermissionCodes.Count(
                Application.Common.Constants.PermissionCodes.IsSensitive));
    }

    public async Task<UserStatisticsResponse> GetStatisticsAsync(
        AccessScope scope, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var soon = now.AddDays(30);

        var query = ExcludePlatformAccounts(ApplyScope(context.Users.AsNoTracking(), scope));

        var byStatus = await query
            .GroupBy(user => user.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var lockedOut = await query.CountAsync(
            user => user.IsLockedOutByAdministrator
                    || (user.LockoutEnd != null && user.LockoutEnd > now),
            cancellationToken);

        var mfaEnabled = await query.CountAsync(user => user.TwoFactorEnabled, cancellationToken);

        var neverSignedIn = await query.CountAsync(user => user.LastLoginAtUtc == null, cancellationToken);

        var expiringSoon = await query.CountAsync(
            user => user.AccessEndsAtUtc != null
                    && user.AccessEndsAtUtc > now
                    && user.AccessEndsAtUtc <= soon,
            cancellationToken);

        int CountOf(UserStatus status) =>
            byStatus.FirstOrDefault(item => item.Status == status)?.Count ?? 0;

        return new UserStatisticsResponse(
            byStatus.Sum(item => item.Count),
            CountOf(UserStatus.Active),
            CountOf(UserStatus.Invited),
            CountOf(UserStatus.Suspended),
            CountOf(UserStatus.Deactivated),
            lockedOut,
            mfaEnabled,
            neverSignedIn,
            expiringSoon,
            byStatus.ToDictionary(item => item.Status.ToString(), item => item.Count, StringComparer.Ordinal));
    }

    public async Task<IReadOnlyList<UserExportRow>> GetExportRowsAsync(
        UserSearchFilter filter, AccessScope scope, bool canSeeSensitiveContact,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var query = ApplyFilters(
            ExcludePlatformAccounts(ApplyScope(context.Users.AsNoTracking(), scope)), filter, now);

        var rows = await ApplySort(query, filter.Sort)
            // An export is capped. Without a ceiling a single click can pull a million rows
            // into memory and out of the building.
            .Take(10_000)
            .Select(user => new
            {
                user.Code,
                user.DisplayName,
                user.Email,
                user.UserName,
                user.Status,
                user.AccountCategory,
                Department = user.Department != null ? user.Department.Name : null,
                Unit = user.OrganisationUnit != null ? user.OrganisationUnit.Name : null,
                user.Designation,
                Manager = user.Manager != null ? user.Manager.DisplayName : null,
                user.TwoFactorEnabled,
                user.MobileNumber,
                user.LastLoginAtUtc,
                user.AccessStartsAtUtc,
                user.AccessEndsAtUtc,
                Roles = user.UserRoles
                    .Where(assignment => assignment.Status == UserRoleAssignmentStatus.Active)
                    .Select(assignment => assignment.Role!.Name ?? assignment.Role.Code)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new UserExportRow(
                row.Code,
                row.DisplayName,
                UserMappingConfig.MaskEmail(row.Email, canSeeSensitiveContact),
                row.UserName ?? string.Empty,
                UserMappingConfig.DescribeStatus(row.Status),
                row.AccountCategory.ToString(),
                row.Department,
                row.Unit,
                row.Designation,
                row.Manager,
                string.Join("; ", row.Roles),
                row.TwoFactorEnabled ? "Yes" : "No",
                row.LastLoginAtUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                row.AccessStartsAtUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                row.AccessEndsAtUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture)))
        ];
    }

    /// <summary>
    /// Removes the PLATFORM accounts from an Organisation directory.
    ///
    /// This is the one place the widened User query filter has to be undone by hand. That
    /// filter carries an "OR TenantId IS NULL" arm so a SuperAdmin can still load their own
    /// record while operating inside an Organisation — see TenantQueryFilter — and without
    /// this the arm would put the platform root account into every Organisation's user list,
    /// showing its name and e-mail address to every TenantAdmin on the platform.
    ///
    /// A platform account is not a member of any Organisation, so it does not belong in any
    /// Organisation's directory, statistics or export. Loading it BY ID still works, which is
    /// what the SuperAdmin's own session needs.
    /// </summary>
    /// <summary>
    /// Directory shape: platform accounts are gone entirely. For the lists, counts and exports.
    /// </summary>
    private static IQueryable<Domain.Entities.User> ExcludePlatformAccounts(
        IQueryable<Domain.Entities.User> query) =>
        query.Where(user => user.TenantId != null);

    /// <summary>
    /// Single-record shape: platform accounts are gone EXCEPT the caller own.
    ///
    /// A SuperAdmin operating inside an Organisation still has to be able to open their own
    /// profile — <c>GET /users/me</c> resolves through here — while a TenantAdmin who guessed
    /// the platform account id gets nothing back.
    /// </summary>
    private static IQueryable<Domain.Entities.User> ExcludePlatformAccountsExceptSelf(
        IQueryable<Domain.Entities.User> query, AccessScope scope) =>
        query.Where(user => user.TenantId != null || user.Id == scope.UserId);

    /// <summary>
    /// Applies the caller NARROWING scope.
    ///
    /// The Organisation filter is already applied underneath by the DbContext. This only ever
    /// narrows further, within one Organisation — a data scope can never widen the set.
    /// </summary>
    private static IQueryable<Domain.Entities.User> ApplyScope(
        IQueryable<Domain.Entities.User> query, AccessScope scope)
    {
        query = ExcludePlatformAccountsExceptSelf(query, scope);

        if (scope.IsTenantWide)
        {
            return query;
        }

        // A caller with only narrowing scopes sees themselves and the people who report to
        // them. Failing closed like this is why an unrecognised scope type is safe.
        return query.Where(user => user.Id == scope.UserId || user.ManagerUserId == scope.UserId);
    }

    private static IQueryable<Domain.Entities.User> ApplyFilters(
        IQueryable<Domain.Entities.User> query, UserSearchFilter filter, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(user =>
                user.DisplayName.ToLower().Contains(term)
                || user.Code.ToLower().Contains(term)
                || (user.Email != null && user.Email.ToLower().Contains(term))
                || (user.UserName != null && user.UserName.ToLower().Contains(term))
                || (user.EmployeeNumber != null && user.EmployeeNumber.ToLower().Contains(term)));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(user => user.Status == filter.Status.Value);
        }

        if (filter.AccountCategory.HasValue)
        {
            query = query.Where(user => user.AccountCategory == filter.AccountCategory.Value);
        }

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(user => user.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.OrganisationUnitId.HasValue)
        {
            query = query.Where(user => user.OrganisationUnitId == filter.OrganisationUnitId.Value);
        }

        if (filter.ManagerUserId.HasValue)
        {
            query = query.Where(user => user.ManagerUserId == filter.ManagerUserId.Value);
        }

        if (filter.MfaRequirement.HasValue)
        {
            query = query.Where(user => user.MfaRequirement == filter.MfaRequirement.Value);
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(user => user.UserRoles.Any(
                assignment => assignment.RoleId == filter.RoleId.Value
                              && assignment.Status == UserRoleAssignmentStatus.Active));
        }

        if (filter.IsLockedOut == true)
        {
            query = query.Where(user =>
                user.IsLockedOutByAdministrator || (user.LockoutEnd != null && user.LockoutEnd > now));
        }
        else if (filter.IsLockedOut == false)
        {
            query = query.Where(user =>
                !user.IsLockedOutByAdministrator && (user.LockoutEnd == null || user.LockoutEnd <= now));
        }

        if (filter.NeverSignedIn == true)
        {
            query = query.Where(user => user.LastLoginAtUtc == null);
        }
        else if (filter.NeverSignedIn == false)
        {
            query = query.Where(user => user.LastLoginAtUtc != null);
        }

        if (filter.AccessEndingBefore.HasValue)
        {
            query = query.Where(user =>
                user.AccessEndsAtUtc != null && user.AccessEndsAtUtc <= filter.AccessEndingBefore.Value);
        }

        if (filter.CreatedFromUtc.HasValue)
        {
            query = query.Where(user => user.CreatedAtUtc >= filter.CreatedFromUtc.Value);
        }

        if (filter.CreatedToUtc.HasValue)
        {
            query = query.Where(user => user.CreatedAtUtc <= filter.CreatedToUtc.Value);
        }

        return query;
    }

    /// <summary>
    /// Sorting, from a closed set of expressions.
    ///
    /// A whitelist rather than dynamic LINQ over an arbitrary string: the sort expression
    /// arrives on a query string, and turning caller text into an expression tree is how a
    /// sort parameter becomes an injection surface.
    /// </summary>
    private static IQueryable<Domain.Entities.User> ApplySort(
        IQueryable<Domain.Entities.User> query, string? sort) =>
        (sort?.Trim().ToLowerInvariant()) switch
        {
            "displayname" or "displayname asc" => query.OrderBy(user => user.DisplayName),
            "displayname desc" => query.OrderByDescending(user => user.DisplayName),
            "code" or "code asc" => query.OrderBy(user => user.Code),
            "code desc" => query.OrderByDescending(user => user.Code),
            "email" or "email asc" => query.OrderBy(user => user.Email),
            "email desc" => query.OrderByDescending(user => user.Email),
            "status" or "status asc" => query.OrderBy(user => user.Status).ThenBy(user => user.DisplayName),
            "status desc" => query.OrderByDescending(user => user.Status).ThenBy(user => user.DisplayName),
            "lastlogin" or "lastloginatutc" => query.OrderByDescending(user => user.LastLoginAtUtc),
            "createdatutc" => query.OrderByDescending(user => user.CreatedAtUtc),
            "updatedatutc asc" => query.OrderBy(user => user.UpdatedAtUtc ?? user.CreatedAtUtc),
            _ => query.OrderByDescending(user => user.UpdatedAtUtc ?? user.CreatedAtUtc)
        };
}
