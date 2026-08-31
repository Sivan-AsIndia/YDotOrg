using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.MySecurity;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The security page a person manages their OWN account from.
///
/// THERE IS NO USER ID IN ANY ROUTE HERE. Every endpoint acts on whoever holds the token, so
/// these calls cannot be aimed at somebody else no matter how the request is shaped. That is
/// why the module is separate from the administrative user endpoints rather than being the same
/// routes with a "self" flag.
///
/// No permission is required either — everybody may look after their own account. What a person
/// may do to ANOTHER account is governed by the permission-gated endpoints on UsersController.
/// </summary>
[Route("api/v1/my-security")]
[Authorize]
// Whatever their Organisation's lifecycle status is, a person must be able to change their own
// password and manage their own second factor. Locking somebody out of their own account while
// their Organisation waits for approval helps nobody.
[AllowedWhileOnboarding]
public sealed class MySecurityController(MySecurityFeatureHandler handler) : ApiControllerBase
{
    /// <summary>Sessions, devices, factors and recent sign-in activity for the caller.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserSecurityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetMySecurityQuery(), cancellationToken));

    /// <summary>
    /// Starts enrolling a second factor.
    ///
    /// THE SHARED SECRET IS RETURNED EXACTLY ONCE — here — so it can be scanned. The factor is
    /// created Pending and stays unusable until <c>confirm</c> proves a code from it works,
    /// which is what stops somebody enrolling a factor they cannot actually use and locking
    /// themselves out.
    /// </summary>
    [HttpPost("mfa/begin")]
    [ProducesResponseType(typeof(ApiResponse<MfaEnrolmentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginMfaEnrolmentAsync(
        [FromBody] BeginMfaEnrolmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return FromResult(await handler.HandleAsync(
            new BeginMfaEnrolmentCommand(request.MethodType, request.Label), cancellationToken));
    }

    [HttpPost("mfa/{methodId:guid}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmMfaEnrolmentAsync(
        Guid methodId, [FromBody] ConfirmMfaEnrolmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return FromResult(await handler.HandleAsync(
            new ConfirmMfaEnrolmentCommand(methodId, request.Code), cancellationToken));
    }

    /// <summary>
    /// Removes a factor.
    ///
    /// Refused when it is the last one and the Organisation policy requires MFA — the request
    /// would otherwise leave the account unable to sign in.
    /// </summary>
    [HttpDelete("mfa/{methodId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeMfaMethodAsync(
        Guid methodId, [FromBody] RevokeMfaMethodRequest? request, CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(
            new RevokeMfaMethodCommand(methodId, request?.Reason), cancellationToken));

    /// <summary>
    /// Issues a fresh batch of backup codes and INVALIDATES every earlier one.
    ///
    /// Codes are shown once, in this response, and stored only as hashes. If they are lost the
    /// only route back is a new batch.
    /// </summary>
    [HttpPost("recovery-codes")]
    [ProducesResponseType(typeof(ApiResponse<RecoveryCodesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateRecoveryCodesAsync(CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GenerateRecoveryCodesCommand(), cancellationToken));

    /// <summary>
    /// Ends one session and leaves the rest alone.
    ///
    /// The whole reason the session list shows a device, a place and a last-active time is so
    /// somebody can spot the one that is not theirs and end THAT one, rather than signing
    /// themselves out of everything including the page they are looking at.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeSessionAsync(
        Guid sessionId, [FromBody] RevokeMySessionRequest? request, CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(
            new RevokeMySessionCommand(sessionId, request?.Reason), cancellationToken));

    /// <summary>
    /// Forgets a remembered device, so it must pass MFA again next time.
    ///
    /// This is the "I signed in on a machine that is not mine" button, and it is why the device
    /// list shows where and when each one was last used.
    /// </summary>
    [HttpDelete("trusted-devices/{deviceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeTrustedDeviceAsync(
        Guid deviceId, [FromBody] RevokeTrustedDeviceRequest? request, CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(
            new RevokeTrustedDeviceCommand(deviceId, request?.Reason), cancellationToken));
}
