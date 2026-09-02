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
    GovernanceQueryHandler governanceQueries) : ApiControllerBase
{
    // =================================================================================
    // Directory and detail
    // =================================================================================

    [HttpGet]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<UserListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] UserSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchUsersQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetUserAsync))]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetUserDetailQuery(id), cancellationToken));

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
        CancellationToken cancellationToken = default) =>
        FromResult(await queries.HandleAsync(new GetPeopleDirectoryQuery(search, take), cancellationToken));

    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAsync(
        [FromQuery] string? search, [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await queries.HandleAsync(new LookupUsersQuery(search, take), cancellationToken));

    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<UserStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetUserStatisticsQuery(), cancellationToken));

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
        [FromQuery] UserSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportUsersQuery(filter), cancellationToken));

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
        [FromBody] CheckUserIdentityRequest request, CancellationToken cancellationToken) =>
        FromResult(await create.HandleAsync(new CheckUserIdentityQuery(request), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.UsersCreate)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(new CreateUserCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(result, nameof(GetUserAsync), new { id = result.Value!.Id },
                "User created.");
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(PermissionCodes.UsersInvite)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendInvitationAsync(
        Guid id, [FromBody] ResendInvitationRequest? request, CancellationToken cancellationToken) =>
        FromResult(
            await create.HandleAsync(
                new ResendUserInvitationCommand(id, request?.Message), cancellationToken),
            "Invitation re-sent.");

    [HttpPost("{id:guid}/revoke-invitation")]
    [HasPermission(PermissionCodes.UsersInvite)]
    [ProducesResponseType(typeof(ApiResponse<CreateUserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitationAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await create.HandleAsync(
                new RevokeUserInvitationCommand(id, request.Reason), cancellationToken),
            "Invitation revoked.");

    // =================================================================================
    // IAM-USR-02 Edit and lifecycle
    // =================================================================================

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.UsersEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new UpdateUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(PermissionCodes.UsersSuspend)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuspendAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new SuspendUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(PermissionCodes.UsersReactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id, [FromBody] ReactivateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new ReactivateUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(PermissionCodes.UsersDeactivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new DeactivateUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(PermissionCodes.UsersCancel)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> WithdrawAsync(
        Guid id, [FromBody] UserLifecycleRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new WithdrawUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(PermissionCodes.UsersUnlock)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockAsync(
        Guid id, [FromBody] UnlockUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new UnlockUserCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/extend-access")]
    [HasPermission(PermissionCodes.UsersEdit)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtendAccessAsync(
        Guid id, [FromBody] ExtendUserAccessRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(new ExtendUserAccessCommand(id, request), cancellationToken));

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
        var result = await lifecycle.HandleAsync(
            new AdminResetPasswordCommand(id, request), cancellationToken);

        var temporaryPassword = TemporaryPasswordAccessor.Take();

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(temporaryPassword))
        {
            Response.Headers.Append("X-Temporary-Password", temporaryPassword);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/force-sign-out")]
    [HasPermission(PermissionCodes.UserSecurityForceSignOut)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceSignOutAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new ForceUserSignOutCommand(id, request.Reason), cancellationToken));

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
        CancellationToken cancellationToken) =>
        FromResult(await userSecurity.HandleAsync(
            new RevokeUserSessionCommand(id, sessionId, request?.Reason), cancellationToken));

    /// <summary>
    /// Forgets one of somebody's remembered devices, so it is challenged again next time.
    /// </summary>
    [HttpDelete("{id:guid}/trusted-devices/{deviceId:guid}")]
    [HasPermission(PermissionCodes.UserSecurityRevokeDevice)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeTrustedDeviceAsync(
        Guid id, Guid deviceId, [FromBody] ReasonRequest? request,
        CancellationToken cancellationToken) =>
        FromResult(await userSecurity.HandleAsync(
            new RevokeUserTrustedDeviceCommand(id, deviceId, request?.Reason), cancellationToken));

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
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await userSecurity.HandleAsync(
            new ResetUserMfaCommand(id, request.Reason), cancellationToken));

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
    public async Task<IActionResult> ExportSecurityAsync(Guid id, CancellationToken cancellationToken) =>
        FileFromResult(await userSecurity.HandleAsync(
            new ExportUserSecurityQuery(id), cancellationToken));

    // =================================================================================
    // IAM-USR-03 Access preview, roles and scopes
    // =================================================================================

    [HttpGet("{id:guid}/access")]
    [HasPermission(PermissionCodes.PermissionsView)]
    [ProducesResponseType(typeof(ApiResponse<UserAccessPreviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccessPreviewAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetUserAccessPreviewQuery(id), cancellationToken));

    /// <summary>What a proposed role change would gain and lose, without committing it.</summary>
    [HttpPost("{id:guid}/access/preview")]
    [HasPermission(PermissionCodes.RolesAssignUsers)]
    [ProducesResponseType(typeof(ApiResponse<UserAccessComparisonResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewAccessAsync(
        Guid id, [FromBody] PreviewUserAccessRequest request, CancellationToken cancellationToken) =>
        FromResult(await access.HandleAsync(new PreviewUserAccessCommand(id, request), cancellationToken));

    [HttpPut("{id:guid}/roles")]
    [HasPermission(PermissionCodes.RolesAssignUsers)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignRolesAsync(
        Guid id, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken) =>
        FromResult(await access.HandleAsync(new AssignUserRolesCommand(id, request), cancellationToken));

    [HttpPut("{id:guid}/data-scopes")]
    [HasPermission(PermissionCodes.PermissionsAssign)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignDataScopesAsync(
        Guid id, [FromBody] AssignUserDataScopesRequest request, CancellationToken cancellationToken) =>
        FromResult(await access.HandleAsync(new AssignUserDataScopesCommand(id, request), cancellationToken));

    // =================================================================================
    // IAM-USR-04 Security, devices and sessions
    // =================================================================================

    [HttpGet("{id:guid}/security")]
    [ProducesResponseType(typeof(ApiResponse<UserSecurityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSecurityAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetUserSecurityQuery(id), cancellationToken));

    // =================================================================================
    // IAM-USR-05 Login identifier change
    // =================================================================================

    [HttpPost("{id:guid}/login-identifier-change")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestIdentifierChangeAsync(
        Guid id,
        [FromBody] RequestLoginIdentifierChangeRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await identifierChange.HandleAsync(
            new RequestLoginIdentifierChangeCommand(id, request), cancellationToken));

    [HttpGet("{id:guid}/login-identifier-change")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LoginIdentifierChangeResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentifierChangesAsync(
        Guid id, CancellationToken cancellationToken) =>
        FromResult(await governanceQueries.HandleAsync(
            new GetLoginIdentifierChangesForUserQuery(id), cancellationToken));

    [HttpPost("login-identifier-change/verify")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyIdentifierChangeAsync(
        [FromBody] VerifyLoginIdentifierChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await identifierChange.HandleAsync(
            new VerifyLoginIdentifierChangeCommand(request), cancellationToken));

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
        [FromBody] DecideLoginIdentifierChangeRequest request, CancellationToken cancellationToken) =>
        FromResult(await identifierChange.HandleAsync(
            new DecideLoginIdentifierChangeCommand(request), cancellationToken));

    [HttpPost("login-identifier-change/{requestId:guid}/apply")]
    [HasPermission(PermissionCodes.UsersChangeLoginIdentifier)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyIdentifierChangeAsync(
        Guid requestId, CancellationToken cancellationToken) =>
        FromResult(await identifierChange.HandleAsync(
            new ApplyLoginIdentifierChangeCommand(requestId), cancellationToken));

    [HttpPost("login-identifier-change/{requestId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelIdentifierChangeAsync(
        Guid requestId, [FromBody] ReasonRequest? request, CancellationToken cancellationToken) =>
        FromResult(await identifierChange.HandleAsync(
            new CancelLoginIdentifierChangeCommand(requestId, request?.Reason), cancellationToken));

    // =================================================================================
    // IAM-USR-06 Bulk administration
    // =================================================================================

    [HttpGet("bulk-actions")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<BulkOperationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchBulkOperationsAsync(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        FromResult(await governanceQueries.HandleAsync(
            new SearchBulkOperationsQuery(pagination), cancellationToken));

    [HttpGet("bulk-actions/{id:guid}")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<BulkOperationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBulkOperationAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await governanceQueries.HandleAsync(new GetBulkOperationQuery(id), cancellationToken));

    /// <summary>
    /// Creates and VALIDATES a bulk job. Nothing is written to the users until it is applied.
    /// </summary>
    [HttpPost("bulk-actions")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateBulkOperationAsync(
        [FromBody] CreateBulkOperationRequest request, CancellationToken cancellationToken) =>
        FromResult(await bulk.HandleAsync(new CreateBulkOperationCommand(request), cancellationToken));

    [HttpPost("bulk-actions/apply")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyBulkOperationAsync(
        [FromBody] ApplyBulkOperationRequest request, CancellationToken cancellationToken) =>
        FromResult(await bulk.HandleAsync(new ApplyBulkOperationCommand(request), cancellationToken));

    [HttpPost("bulk-actions/{id:guid}/cancel")]
    [HasPermission(PermissionCodes.UsersBulkAdminister)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelBulkOperationAsync(
        Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await bulk.HandleAsync(
            new CancelBulkOperationCommand(id, request.Reason), cancellationToken));
}
