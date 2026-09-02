using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.MyProfile;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Queries.UserQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The caller's own profile.
///
/// THERE IS NO USER ID IN THIS ROUTE, exactly as on <see cref="MySecurityController"/>, and for
/// the same reason: the server acts on whoever holds the token, so the request cannot be aimed
/// at anybody else however it is shaped. That is what makes it safe to serve with no permission
/// gate — and a permission gate is precisely what was in the way.
///
/// WHY THIS IS NOT <c>GET /users/me</c> ANY MORE. That action lived on
/// <see cref="UsersController"/>, which carries
/// <c>[Authorize(Policy = PolicyNames.TenantContextRequired)]</c> at class level. Authorize
/// attributes on a controller and an action are ANDed, never overridden, so every route on that
/// controller — the caller's own record included — required a resolved Organisation. Two people
/// hit that:
///
///   - A SUPERADMIN WHO HAS NOT YET CHOSEN AN ORGANISATION. Their token is Global scope with no
///     tenant, so <c>TenantContextRequirement</c> fails and the answer is 403 with the generic
///     "You do not have permission to perform this action." On the one screen whose entire job
///     is to show a person their own profile.
///   - ANYBODY WITHOUT <c>iam.users.view</c>. The profile screen reached its own record through
///     the administrative directory search, which is permission-gated, so ten of the fifteen
///     roles were refused their own profile. The route itself was deliberately left unguarded
///     for them; the data call was not.
///
/// The query behind it is unchanged and was already written for this: <c>GetDetailAsync</c>
/// keeps the platform account visible to itself (see <c>ExcludePlatformAccountsExceptSelf</c>)
/// and the widened User query filter keeps a null-tenant row readable with no Organisation
/// resolved. Nothing here widens what anybody can see — it stops a check that was never about
/// this record from refusing it.
/// </summary>
[Route("api/v1/my-profile")]
[Authorize]
// Whatever their Organisation's lifecycle status is, a person must be able to see who the
// platform thinks they are. Refusing that while their Organisation waits for approval helps
// nobody, and it is the screen they land on.
[AllowedWhileOnboarding]
public sealed class MyProfileController(
    UserQueryHandler queries, MyProfileFeatureHandler handler) : ApiControllerBase
{
    /// <summary>The caller's own record: identity, organisation, roles and data scopes.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetMyProfileQuery(), cancellationToken));

    /// <summary>
    /// Saves the caller's own profile.
    ///
    /// THE WRITE HALF OF THE SAME ARGUMENT AS THE READ ABOVE. Edit profile on a person's own
    /// record used to call <c>PUT /users/{id}</c>, which needs <c>iam.users.edit</c> AND a
    /// resolved Organisation — so most roles, and every root user who had not yet picked an
    /// Organisation, were refused permission to change their own display name.
    ///
    /// It accepts five fields and no more; department, manager, account category, access window
    /// and MFA requirement are administrative decisions and are simply not on the request.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        [FromBody] UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return FromResult(await handler.HandleAsync(
            new UpdateMyProfileCommand(request), cancellationToken));
    }
}
