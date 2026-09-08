using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Governance.Queries.GovernanceQueries;
using YDot.IAM.Application.Features.Users.Commands.BulkUserAdministration;
using YDot.IAM.Application.Features.Users.Commands.CreateUser;
using YDot.IAM.Application.Features.Users.Commands.LoginIdentifierChange;
using YDot.IAM.Application.Features.Users.Commands.UserAccess;
using YDot.IAM.Application.Features.Users.Commands.UserLifecycle;
using YDot.IAM.Application.Features.Users.Commands.UserSecurity;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Queries.UserQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Users: IAM-USR-01 through IAM-USR-06.
///
/// EVERY ACTION IS TENANT-SCOPED WITHOUT SAYING SO. There is no organisation parameter on any
/// route here, and there must not be: the Organisation comes from the token, the query filter
/// applies underneath, and a user in another Organisation is simply not found. A TenantAdmin
/// and a SuperAdmin who has selected that Organisation call exactly the same endpoints, which
/// is what section 48 of the brief asks for.
/// </summary>
[Route("api/v1/users")]
[Authorize(Policy = PolicyNames.TenantContextRequired)]
public sealed class UsersController(
    CreateUserCommandHandler create,
    UserLifecycleCommandHandler lifecycle,
    UserAccessCommandHandler access,
    LoginIdentifierChangeCommandHandler identifierChange,
    BulkUserAdministrationCommandHandler bulk,
    UserSecurityCommandHandler userSecurity,
    UserQueryHandler queries,
    GovernanceQueryHandler governanceQueries,
    ILogger<UsersController> logger) : ApiControllerBase
{
    // =================================================================================
    // Directory and detail
    // =================================================================================

    [HttpGet]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<UserListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] UserSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching users.");

        var result = await queries.HandleAsync(new SearchUsersQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User search failed.");

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetUserAsync))]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting user. UserId: {UserId}", id);

        var result = await queries.HandleAsync(new GetUserDetailQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get user. UserId: {UserId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// The Organisation's people, for a picker. Every member may read it.
    ///
    /// NO PERMISSION GATE BEYOND AUTHENTICATION, and that is the point: the controls that need
    /// it - naming a campaign owner, routing a lead - are used by people who are not user
    /// administrators. It carries id, name and staff code, and nothing else.
    /// </summary>
    [HttpGet("directory")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PersonLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DirectoryAsync(
        [FromQuery] string? search, [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Getting people directory.");

        var result = await queries.HandleAsync(
            new GetPeopleDirectoryQuery(search, take), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get people directory.");

        return FromResult(result);
    }

    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(
        [FromQuery] string? search, [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Looking up users.");

        var result = await queries.HandleAsync(
            new LookupUsersQuery(search, take), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User lookup failed.");

        return FromResult(result);
    }

    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<UserStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting user statistics.");

        var result = await queries.HandleAsync(new GetUserStatisticsQuery(), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get user statistics.");

        return FromResult(result);
    }

    // THE CALLER'S OWN PROFILE HAS MOVED to GET /api/v1/my-profile — see MyProfileController.
    //
    // It could not stay here. This controller requires TenantContextRequired at class level,
    // and an Authorize attribute on an action ANDs with the controller's rather than replacing
    // it, so "my own record" demanded a resolved Organisation like every administrative route
    // around it. A SuperAdmin who had not yet chosen an Organisation was refused their own
    // profile with a flat 403, which is what the profile screen surfaced as "Could not load
    // that person — You do not have permission to perform this action."

    [HttpGet("export")]
    [HasPermission(PermissionCodes.UsersExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] UserSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting users.");

        var result = await queries.HandleAsync(new ExportUsersQuery(filter), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User export failed.");

        return FileFromResult(result);
    }

    // =================================================================================
    // IAM-USR-01 Invite or create
    // =================================================================================

    /// <summary>
    /// Whether an e-mail address or username is free, before the create form is submitted.
    ///
    /// Scoped to the caller's Organisation, which is exactly the scope the uniqueness rule uses:
    /// the same address may exist in another Organisation and that is not a clash. It never
    /// names the holder of a taken value - that would turn the create form into a directory
    /// lookup for anybody who can reach it.
    /// </summary>
    [HttpPost("check-identity")]
    [HasPermission(PermissionCodes.UsersCreate)]
    [ProducesResponseType(typeof(ApiResponse<CheckUserIdentityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIdentityAsync(
        [FromBody] CheckUserIdentityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Checking user identity availability.");

        var result = await create.HandleAsync(
            new CheckUserIdentityQuery(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User identity availability check failed.");

        return FromResult(result);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.UsersCreate)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating user.");

        var result = await create.HandleAsync(new CreateUserCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("User creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("User created successfully. UserId: {UserId}", result.Value!.Id);

        return CreatedFromResult(result, nameof(GetUserAsync), new { id = result.Value.Id },
            "User created.");
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(PermissionCodes.UsersInvite)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendInvitationAsync(
        Guid id, [FromBody] ResendInvitationRequest? request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resending user invitation. UserId: {UserId}", id);

        var result = await create.HandleAsync(
            new ResendUserInvitationCommand(id, request?.Message), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User invitation resend failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User invitation resent successfully. UserId: {UserId}", id);

        return FromResult(result, "Invitation re-sent.");
    }

    [HttpPost("{id:guid}/revoke-invitation")]
    [HasPermission(PermissionCodes.UsersInvite)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitationAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Revoking user invitation. UserId: {UserId}", id);

        var result = await create.HandleAsync(
            new RevokeUserInvitationCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User invitation revocation failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User invitation revoked successfully. UserId: {UserId}", id);

        return FromResult(result, "Invitation revoked.");
    }

    // =================================================================================
    // IAM-USR-02 Edit and lifecycle
    // =================================================================================

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.UsersEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Updating user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new UpdateUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User update failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User updated successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(PermissionCodes.UsersSuspend)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuspendAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Suspending user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new SuspendUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User suspension failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User suspended successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(PermissionCodes.UsersReactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id, [FromBody] ReactivateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Reactivating user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new ReactivateUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User reactivation failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User reactivated successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.UsersDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deactivating user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new DeactivateUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User deactivation failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User deactivated successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(PermissionCodes.UsersCancel)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> WithdrawAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Withdrawing user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new WithdrawUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User withdrawal failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User withdrawn successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(PermissionCodes.UsersUnlock)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockAsync(
        Guid id, [FromBody] UnlockUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Unlocking user. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new UnlockUserCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User unlock failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User unlocked successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/extend-access")]
    [HasPermission(PermissionCodes.UsersEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtendAccessAsync(
        Guid id, [FromBody] ExtendUserAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Extending user access. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new ExtendUserAccessCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User access extension failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User access extended successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// An administrator resetting a password.
    ///
    /// When a temporary password is issued it comes back in the <c>X-Temporary-Password</c>
    /// header, ONCE. It is never stored and never e-mailed — a temporary password sent over
    /// the same channel it was meant to protect is not a control.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(PermissionCodes.UsersResetPassword)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPasswordAsync(
        Guid id, [FromBody] AdminResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Resetting user password. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new AdminResetPasswordCommand(id, request), cancellationToken);

        var temporaryPassword = TemporaryPasswordAccessor.Take();

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(temporaryPassword))
        {
            Response.Headers.Append("X-Temporary-Password", temporaryPassword);
            logger.LogInformation("Temporary password generated for user password reset. UserId: {UserId}", id);
        }

        if (result.IsFailure)
            logger.LogWarning("User password reset failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User password reset completed successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("{id:guid}/force-sign-out")]
    [HasPermission(PermissionCodes.UserSecurityForceSignOut)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceSignOutAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Forcing user sign-out. UserId: {UserId}", id);

        var result = await lifecycle.HandleAsync(
            new ForceUserSignOutCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Forced user sign-out failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User signed out successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Ends ONE of somebody's sessions.
    ///
    /// Signing a person out of everything is the right answer to a compromised account and the
    /// wrong one to a laptop left at an airport. This is the narrow version, so the response
    /// can match what actually happened.
    /// </summary>
    [HttpDelete("{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(PermissionCodes.UserSecurityRevokeSession)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeSessionAsync(
        Guid id, Guid sessionId, [FromBody] ReasonRequest? request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Revoking user session. UserId: {UserId}, SessionId: {SessionId}", id, sessionId);

        var result = await userSecurity.HandleAsync(
            new RevokeUserSessionCommand(id, sessionId, request?.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User session revocation failed. UserId: {UserId}, SessionId: {SessionId}", id, sessionId);
        else
            logger.LogInformation("User session revoked successfully. UserId: {UserId}, SessionId: {SessionId}", id, sessionId);

        return FromResult(result);
    }

    /// <summary>
    /// Forgets one of somebody's remembered devices, so it is challenged again next time.
    /// </summary>
    [HttpDelete("{id:guid}/trusted-devices/{deviceId:guid}")]
    [HasPermission(PermissionCodes.UserSecurityRevokeDevice)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeTrustedDeviceAsync(
        Guid id, Guid deviceId, [FromBody] ReasonRequest? request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Revoking user trusted device. UserId: {UserId}, DeviceId: {DeviceId}", id, deviceId);

        var result = await userSecurity.HandleAsync(
            new RevokeUserTrustedDeviceCommand(id, deviceId, request?.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User trusted device revocation failed. UserId: {UserId}, DeviceId: {DeviceId}", id, deviceId);
        else
            logger.LogInformation("User trusted device revoked successfully. UserId: {UserId}, DeviceId: {DeviceId}", id, deviceId);

        return FromResult(result);
    }

    /// <summary>
    /// Clears every second factor on an account so the person enrols again.
    ///
    /// The case is a lost phone with the authenticator on it: they cannot complete MFA, and
    /// they cannot remove the factor themselves because removing it needs a code from it.
    /// Somebody has to break that loop. Sessions, remembered devices and backup codes go with
    /// the factors — leaving any of them live would leave a way round the reset.
    /// </summary>
    [HttpPost("{id:guid}/reset-mfa")]
    [HasPermission(PermissionCodes.UserSecurityResetMfa)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetMfaAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Resetting user MFA. UserId: {UserId}", id);

        var result = await userSecurity.HandleAsync(
            new ResetUserMfaCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User MFA reset failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User MFA reset successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// The security position of one account as a file.
    ///
    /// No secret leaves in it — not a hash, not a backup code, not a session token. It says a
    /// factor of a given kind exists and when it was last used, which is what "who could sign
    /// in as this person, and when did they last do it" actually needs.
    /// </summary>
    [HttpGet("{id:guid}/security/export")]
    [HasPermission(PermissionCodes.UserSecurityView)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportSecurityAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting user security information. UserId: {UserId}", id);

        var result = await userSecurity.HandleAsync(
            new ExportUserSecurityQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User security export failed. UserId: {UserId}", id);

        return FileFromResult(result);
    }

    // =================================================================================
    // IAM-USR-03 Access preview, roles and scopes
    // =================================================================================

    [HttpGet("{id:guid}/access")]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<UserAccessPreviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccessPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting user access preview. UserId: {UserId}", id);

        var result = await queries.HandleAsync(
            new GetUserAccessPreviewQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get user access preview. UserId: {UserId}", id);

        return FromResult(result);
    }

    /// <summary>What a proposed role change would gain and lose, without committing it.</summary>
    [HttpPost("{id:guid}/access/preview")]
    [HasPermission(PermissionCodes.RolesAssignUsers)]
    [ProducesResponseType(typeof(ApiResponse<UserAccessComparisonResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewAccessAsync(
        Guid id, [FromBody] PreviewUserAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Previewing user access changes. UserId: {UserId}", id);

        var result = await access.HandleAsync(
            new PreviewUserAccessCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User access preview failed. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPut("{id:guid}/roles")]
    [HasPermission(PermissionCodes.RolesAssignUsers)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignRolesAsync(
        Guid id, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Assigning roles to user. UserId: {UserId}", id);

        var result = await access.HandleAsync(
            new AssignUserRolesCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User role assignment failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User roles assigned successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPut("{id:guid}/data-scopes")]
    [HasPermission(PermissionCodes.PermissionsAssign)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignDataScopesAsync(
        Guid id, [FromBody] AssignUserDataScopesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Assigning data scopes to user. UserId: {UserId}", id);

        var result = await access.HandleAsync(
            new AssignUserDataScopesCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("User data scope assignment failed. UserId: {UserId}", id);
        else
            logger.LogInformation("User data scopes assigned successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    // =================================================================================
    // IAM-USR-04 Security, devices and sessions
    // =================================================================================

    [HttpGet("{id:guid}/security")]
    [ProducesResponseType(typeof(ApiResponse<UserSecurityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSecurityAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting user security information. UserId: {UserId}", id);

        var result = await queries.HandleAsync(
            new GetUserSecurityQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get user security information. UserId: {UserId}", id);

        return FromResult(result);
    }

    // =================================================================================
    // IAM-USR-05 Login identifier change
    // =================================================================================

    [HttpPost("{id:guid}/login-identifier-change")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestIdentifierChangeAsync(
        Guid id,
        [FromBody] RequestLoginIdentifierChangeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Requesting login identifier change. UserId: {UserId}", id);

        var result = await identifierChange.HandleAsync(
            new RequestLoginIdentifierChangeCommand(id, request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Login identifier change request failed. UserId: {UserId}", id);
        else
            logger.LogInformation("Login identifier change requested successfully. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpGet("{id:guid}/login-identifier-change")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LoginIdentifierChangeResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentifierChangesAsync(
        Guid id, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting login identifier changes. UserId: {UserId}", id);

        var result = await governanceQueries.HandleAsync(
            new GetLoginIdentifierChangesForUserQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get login identifier changes. UserId: {UserId}", id);

        return FromResult(result);
    }

    [HttpPost("login-identifier-change/verify")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyIdentifierChangeAsync(
        [FromBody] VerifyLoginIdentifierChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Verifying login identifier change.");

        var result = await identifierChange.HandleAsync(
            new VerifyLoginIdentifierChangeCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Login identifier change verification failed.");
        else
            logger.LogInformation("Login identifier change verified successfully.");

        return FromResult(result);
    }

    /// <summary>
    /// A second person approving the change.
    ///
    /// The IndependentApprover policy is the coarse half of the check; the handler also
    /// refuses when the caller raised the request or is its subject.
    /// </summary>
    [HttpPost("login-identifier-change/decide")]
    [HasPermission(PermissionCodes.UsersChangeLoginIdentifier)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DecideIdentifierChangeAsync(
        [FromBody] DecideLoginIdentifierChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Deciding login identifier change.");

        var result = await identifierChange.HandleAsync(
            new DecideLoginIdentifierChangeCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Login identifier change decision failed.");
        else
            logger.LogInformation("Login identifier change decision completed successfully.");

        return FromResult(result);
    }

    [HttpPost("login-identifier-change/{requestId:guid}/apply")]
    [HasPermission(PermissionCodes.UsersChangeLoginIdentifier)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyIdentifierChangeAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Applying login identifier change. RequestId: {RequestId}", requestId);

        var result = await identifierChange.HandleAsync(
            new ApplyLoginIdentifierChangeCommand(requestId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Login identifier change application failed. RequestId: {RequestId}", requestId);
        else
            logger.LogInformation("Login identifier change applied successfully. RequestId: {RequestId}", requestId);

        return FromResult(result);
    }

    [HttpPost("login-identifier-change/{requestId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelIdentifierChangeAsync(
        Guid requestId, [FromBody] ReasonRequest? request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Cancelling login identifier change. RequestId: {RequestId}", requestId);

        var result = await identifierChange.HandleAsync(
            new CancelLoginIdentifierChangeCommand(requestId, request?.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Login identifier change cancellation failed. RequestId: {RequestId}", requestId);
        else
            logger.LogInformation("Login identifier change cancelled successfully. RequestId: {RequestId}", requestId);

        return FromResult(result);
    }

    // =================================================================================
    // IAM-USR-06 Bulk administration
    // =================================================================================

    [HttpGet("bulk-actions")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<BulkOperationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchBulkOperationsAsync(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching bulk user administration operations.");

        var result = await governanceQueries.HandleAsync(
            new SearchBulkOperationsQuery(pagination), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Bulk user administration operation search failed.");

        return FromResult(result);
    }

    [HttpGet("bulk-actions/{id:guid}")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<BulkOperationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBulkOperationAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting bulk user administration operation. BulkOperationId: {BulkOperationId}", id);

        var result = await governanceQueries.HandleAsync(
            new GetBulkOperationQuery(id), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Failed to get bulk user administration operation. BulkOperationId: {BulkOperationId}", id);

        return FromResult(result);
    }

    /// <summary>
    /// Creates and VALIDATES a bulk job. Nothing is written to the users until it is applied.
    /// </summary>
    [HttpPost("bulk-actions")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateBulkOperationAsync(
        [FromBody] CreateBulkOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Creating bulk user administration operation.");

        var result = await bulk.HandleAsync(
            new CreateBulkOperationCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Bulk user administration operation creation failed.");
        else
            logger.LogInformation("Bulk user administration operation created successfully.");

        return FromResult(result);
    }

    [HttpPost("bulk-actions/apply")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyBulkOperationAsync(
        [FromBody] ApplyBulkOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Applying bulk user administration operation.");

        var result = await bulk.HandleAsync(
            new ApplyBulkOperationCommand(request), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Bulk user administration operation application failed.");
        else
            logger.LogInformation("Bulk user administration operation applied successfully.");

        return FromResult(result);
    }

    [HttpPost("bulk-actions/{id:guid}/cancel")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelBulkOperationAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Cancelling bulk user administration operation. BulkOperationId: {BulkOperationId}", id);

        var result = await bulk.HandleAsync(
            new CancelBulkOperationCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Bulk user administration operation cancellation failed. BulkOperationId: {BulkOperationId}", id);
        else
            logger.LogInformation("Bulk user administration operation cancelled successfully. BulkOperationId: {BulkOperationId}", id);

        return FromResult(result);
    }
}