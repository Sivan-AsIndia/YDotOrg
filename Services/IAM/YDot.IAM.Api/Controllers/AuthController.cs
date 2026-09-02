using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using YDot.IAM.Api.Security;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.Commands.AcceptInvitation;
using YDot.IAM.Application.Features.Authentication.Commands.MfaVerification;
using YDot.IAM.Application.Features.Authentication.Commands.PasswordRecovery;
using YDot.IAM.Application.Features.Authentication.Commands.Reauthenticate;
using YDot.IAM.Application.Features.Authentication.Commands.SelectTenant;
using YDot.IAM.Application.Features.Authentication.Commands.SignIn;
using YDot.IAM.Application.Features.Authentication.Commands.Tokens;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Queries.AuthenticationViews;
using YDot.IAM.Application.Features.Menus.DTOs;
using YDot.IAM.Application.Features.Menus.Queries.Navigation;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Authentication: sign-in, MFA, tokens, invitation acceptance, password recovery,
/// re-authentication and SuperAdmin Organisation switching.
///
/// THE ROUTE PREFIX IS <c>/api/v1/users</c> RATHER THAN <c>/api/v1/auth</c>. That is not an
/// accident: it is the route the Angular client already calls, and it matches the section 01
/// traceability table where every screen hangs off <c>/api/v1/users</c>. The organisation
/// switching endpoints are the exception and live under <c>/api/v1/auth</c>, because section
/// 13 of the brief names that path explicitly.
///
/// THE REFRESH TOKEN NEVER APPEARS IN A RESPONSE BODY. Every action that issues one strips it
/// out and writes it into an HttpOnly cookie instead — see <see cref="StripRefreshToken"/>.
/// </summary>
[Route("api/v1/users")]
public sealed class AuthController(
    SignInCommandHandler signIn,
    MfaVerificationCommandHandler mfaVerification,
    TokenCommandHandler tokens,
    AcceptInvitationCommandHandler invitations,
    PasswordRecoveryCommandHandler passwordRecovery,
    ReauthenticationCommandHandler reauthentication,
    SelectTenantCommandHandler tenantSelection,
    AuthenticationViewQueryHandler views,
    NavigationQueryHandler navigation,
    RefreshTokenCookieWriter cookies,
    IOptions<SecuritySettings> securityOptions) : ApiControllerBase
{
    private readonly SecuritySettings _security = securityOptions.Value;

    // =================================================================================
    // IAM-AUTH-01 Sign in
    // =================================================================================

    /// <summary>
    /// Signs in.
    ///
    /// Anonymous by necessity. The Organisation is resolved from the host by the middleware,
    /// never from this body.
    /// </summary>
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SignInResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status423Locked)]
    public async Task<IActionResult> SignInAsync(
        [FromBody] SignInRequest request, CancellationToken cancellationToken)
    {
        // The trusted-device cookie is read here rather than expected in the body: it is
        // HttpOnly, so the client could not send it even if it wanted to.
        var withDeviceToken = request with
        {
            TrustedDeviceToken = request.TrustedDeviceToken ?? cookies.ReadTrustedDevice(Request)
        };

        // Opened BEFORE the handler runs, in this frame, so the slot the handler writes into is
        // one this method still holds a reference to. See TrustedDeviceTokenAccessor.
        TrustedDeviceTokenAccessor.Begin();

        var result = await signIn.HandleAsync(new SignInCommand(withDeviceToken), cancellationToken);

        // "Remember this device" on the sign-in form ends here: the row is written by the
        // handler and the plaintext half of it becomes an HttpOnly cookie, which is the only
        // thing that makes the next sign-in recognise this browser.
        var deviceToken = TrustedDeviceTokenAccessor.Take();

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(deviceToken))
        {
            cookies.WriteTrustedDevice(Response, deviceToken, _security.TrustedDeviceDays);
        }

        return IssueTokens(result);
    }

    // =================================================================================
    // IAM-AUTH-05 MFA challenge
    // =================================================================================

    [HttpPost("mfa-challenge/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SignInResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyMfaAsync(
        [FromBody] VerifyMfaRequest request, CancellationToken cancellationToken)
    {
        TrustedDeviceTokenAccessor.Begin();

        var result = await mfaVerification.HandleAsync(new VerifyMfaCommand(request), cancellationToken);

        // A device the person asked to remember gets its token written as a second HttpOnly
        // cookie, for the same reason as the refresh token.
        var deviceToken = TrustedDeviceTokenAccessor.Take();

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(deviceToken))
        {
            cookies.WriteTrustedDevice(Response, deviceToken, _security.TrustedDeviceDays);
        }

        return IssueTokens(result);
    }

    [HttpPost("mfa-challenge/resend")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<MfaChallengeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendMfaChallengeAsync(
        [FromBody] ResendMfaChallengeRequest request, CancellationToken cancellationToken) =>
        FromResult(await mfaVerification.HandleAsync(
            new ResendMfaChallengeCommand(request), cancellationToken));

    /// <summary>
    /// Abandons a half-finished sign-in.
    ///
    /// Retires the challenge immediately rather than letting it expire, so a code already
    /// delivered to a phone or an inbox stops working the moment the person backs out.
    /// </summary>
    [HttpPost("mfa-challenge/cancel")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelMfaChallengeAsync(
        [FromBody] CancelMfaChallengeRequest request, CancellationToken cancellationToken) =>
        FromResult(await mfaVerification.HandleAsync(
            new CancelMfaChallengeCommand(request), cancellationToken));

    [HttpPost("mfa-challenge/recovery-code")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SignInResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RedeemRecoveryCodeAsync(
        [FromBody] RedeemRecoveryCodeRequest request, CancellationToken cancellationToken) =>
        IssueTokens(await mfaVerification.HandleAsync(
            new RedeemRecoveryCodeCommand(request), cancellationToken));

    // =================================================================================
    // Tokens
    // =================================================================================

    /// <summary>
    /// Exchanges a refresh token for a new pair.
    ///
    /// Anonymous, because the access token has by definition expired by the time this is
    /// called. The refresh token in the HttpOnly cookie is the credential.
    /// </summary>
    [HttpPost("tokens/refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync(
        [FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        var presented = cookies.Read(Request, request?.RefreshToken);

        var result = await tokens.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(presented)), cancellationToken);

        if (result.IsFailure)
        {
            // The session is over, so the cookie is cleared rather than left to be presented
            // again on every subsequent request.
            cookies.Clear(Response);
            return FromResult(result);
        }

        var issued = result.Value!;
        cookies.Write(Response, issued.RefreshToken, issued.RefreshTokenExpiresAtUtc);

        return FromResult(Result.Success(issued with { RefreshToken = string.Empty }));
    }

    [HttpPost("sign-out")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<Application.DTOs.OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SignOutAsync(
        [FromBody] SignOutRequest? request, CancellationToken cancellationToken)
    {
        var result = await tokens.HandleAsync(
            new SignOutCommand(request ?? new SignOutRequest()), cancellationToken);

        cookies.Clear(Response);

        return FromResult(result);
    }

    /// <summary>
    /// The current session, for the idle-timeout banner.
    ///
    /// The client counts down against the SERVER clock rather than its own, which drifts and
    /// which the person can change.
    /// </summary>
    [HttpGet("session")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<SessionStatusResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessionAsync(CancellationToken cancellationToken) =>
        FromResult(await tokens.GetSessionStatusAsync(cancellationToken));

    // =================================================================================
    // Section 13 SuperAdmin organisation switching
    // =================================================================================

    /// <summary>
    /// SuperAdmin entering an Organisation operating context.
    ///
    /// Re-issues the access token against the SAME session, pointed at the chosen Organisation.
    /// The SuperAdmin user row is never touched.
    /// </summary>
    [HttpPost("/api/v1/auth/select-tenant")]
    [Authorize(Policy = PolicyNames.SuperAdminOnly)]
    [ProducesResponseType(typeof(ApiResponse<SelectTenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SelectTenantAsync(
        [FromBody] SelectTenantRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await tenantSelection.HandleAsync(new SelectTenantCommand(request), cancellationToken),
            "Organisation selected.");

    /// <summary>
    /// SuperAdmin leaving an Organisation and returning to platform scope.
    ///
    /// The counterpart to select-tenant, and the reason it exists: without it, entering an
    /// Organisation was a one-way door for the rest of the session. Re-issues the token against
    /// the SAME session with no operating Organisation, so tenant_id stops stamping writes and
    /// labelling audit rows. The SuperAdmin user row is never touched, here or on the way in.
    ///
    /// Takes no body - there is only one place to go back to.
    /// </summary>
    [HttpPost("/api/v1/auth/exit-tenant")]
    [Authorize(Policy = PolicyNames.SuperAdminOnly)]
    [ProducesResponseType(typeof(ApiResponse<SelectTenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExitTenantAsync(CancellationToken cancellationToken) =>
        FromResult(
            await tenantSelection.HandleAsync(new ExitTenantCommand(), cancellationToken),
            "You are back at platform level.");

    /// <summary>The Organisations the caller may enter. Empty for a Tenant user.</summary>
    [HttpGet("/api/v1/auth/selectable-tenants")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TenantOptionResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSelectableTenantsAsync(CancellationToken cancellationToken) =>
        FromResult(await tenantSelection.HandleAsync(new GetSelectableTenantsQuery(), cancellationToken));

    // =================================================================================
    // IAM-AUTH-02 Accept invitation and activate account
    // =================================================================================

    /// <summary>
    /// What the activation screen shows before a password is typed.
    ///
    /// Anonymous and deliberately thin: somebody holding a token should learn only enough to
    /// decide whether to continue.
    /// </summary>
    [HttpGet("accept-invitation-and-activate-account")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<InvitationPreviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewInvitationAsync(
        [FromQuery] string token, CancellationToken cancellationToken) =>
        FromResult(await invitations.HandleAsync(new PreviewInvitationQuery(token), cancellationToken));

    [HttpPost("accept-invitation-and-activate-account")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AcceptInvitationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitationAsync(
        [FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var result = await invitations.HandleAsync(
            new AcceptInvitationCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            return FromResult(result);
        }

        var response = result.Value!;

        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            cookies.Write(
                Response, response.RefreshToken, DateTimeOffset.UtcNow.AddDays(14));
        }

        return FromResult(
            Result.Success(response with { RefreshToken = null }),
            "Your account is active.");
    }

    /// <summary>
    /// Starts enrolling a second factor while activating an invited account.
    ///
    /// AUTHORISED BY THE INVITATION TOKEN, because the person has no session yet - that is the
    /// whole point of the screen they are on. The token names exactly one user in exactly one
    /// Organisation, so it is a narrower credential than a session rather than a looser one.
    ///
    /// The shared secret comes back once, here. The method is created Pending and does not count
    /// as a factor until <c>verify-mfa-method</c> proves a code from it works.
    /// </summary>
    [HttpPost("accept-invitation-and-activate-account/begin-mfa-enrolment")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<MfaEnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BeginInvitationMfaEnrolmentAsync(
        [FromBody] BeginInvitationMfaEnrolmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await invitations.HandleAsync(
            new BeginInvitationMfaEnrolmentCommand(request), cancellationToken));

    /// <summary>
    /// Confirms the factor enrolled during activation, before the account is activated.
    ///
    /// Proving it here rather than afterwards is what stops somebody enrolling a factor they
    /// cannot actually use and then being locked out by it on their very first sign-in.
    /// </summary>
    [HttpPost("accept-invitation-and-activate-account/verify-mfa-method")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyInvitationMfaEnrolmentAsync(
        [FromBody] VerifyInvitationMfaEnrolmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await invitations.HandleAsync(
            new VerifyInvitationMfaEnrolmentCommand(request), cancellationToken));

    /// <summary>
    /// Asks for a replacement invitation.
    ///
    /// The current link stops working immediately. Issuing a new one while the old one still
    /// opened the door would defeat the point of asking for a replacement.
    ///
    /// The answer is the same whether or not the token was real, for the same reason
    /// forgot-password is: a different answer would make this a way to test invitation tokens.
    /// </summary>
    [HttpPost("accept-invitation-and-activate-account/request-new-invitation")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestNewInvitationAsync(
        [FromBody] RequestNewInvitationRequest request, CancellationToken cancellationToken) =>
        FromResult(await invitations.HandleAsync(
            new RequestNewInvitationCommand(request), cancellationToken));

    /// <summary>
    /// Leaves the activation flow without completing it.
    ///
    /// The invitation stays usable: somebody who backs out to check a detail with their
    /// administrator should be able to return to the same link. What is recorded is that they
    /// reached the screen and stopped, which is worth knowing when an invitation is never used.
    /// </summary>
    [HttpPost("accept-invitation-and-activate-account/cancel")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelActivationAsync(
        [FromBody] CancelActivationRequest request, CancellationToken cancellationToken) =>
        FromResult(await invitations.HandleAsync(
            new CancelActivationCommand(request), cancellationToken));

    // =================================================================================
    // IAM-AUTH-03 and IAM-AUTH-04 Password recovery
    // =================================================================================

    /// <summary>
    /// Starts a reset.
    ///
    /// Always answers the same whether or not the account exists — a different reply would be
    /// a free way to test which addresses are registered.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ForgotPasswordResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(
            new ForgotPasswordCommand(request), cancellationToken));

    /// <summary>
    /// Whether a recovery link is still usable, and the rules the new password must satisfy.
    ///
    /// CALLED BEFORE THE FORM IS DRAWN. Without it, somebody carefully chooses a password,
    /// presses Save, and only then learns the link expired an hour ago.
    ///
    /// It names nobody: a person holding a stale link has proved nothing, so the reply carries
    /// no address and no display name.
    /// </summary>
    [HttpGet("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ResetPasswordViewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetResetPasswordViewAsync(
        [FromQuery] string token, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(
            new GetResetPasswordViewQuery(token), cancellationToken));

    /// <summary>
    /// Sends a fresh recovery link when the current one has lapsed.
    ///
    /// Same generic reply as forgot-password, for the same reason: a reply that differed for a
    /// known address would make this a way of testing which addresses are registered.
    /// </summary>
    [HttpPost("reset-password/request-new-link")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ForgotPasswordResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestNewRecoveryLinkAsync(
        [FromBody] RequestNewRecoveryLinkRequest request, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(
            new RequestNewRecoveryLinkCommand(request), cancellationToken));

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PasswordOperationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await passwordRecovery.HandleAsync(
            new ResetPasswordCommand(request), cancellationToken);

        // Every session died in the handler, so the cookie must go too.
        if (result.IsSuccess)
        {
            cookies.Clear(Response);
        }

        return FromResult(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<PasswordOperationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(
            new ChangePasswordCommand(request), cancellationToken));

    /// <summary>Confirms an e-mail address from the link.</summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PasswordOperationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmEmailAsync(
        [FromQuery] string token, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(new ConfirmEmailCommand(token), cancellationToken));

    /// <summary>The password rules, so the client strength meter matches the server.</summary>
    [HttpGet("password-policy")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PasswordPolicyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPasswordPolicyAsync(CancellationToken cancellationToken) =>
        FromResult(await views.HandleAsync(new GetPasswordPolicyQuery(), cancellationToken));

    // =================================================================================
    // IAM-AUTH-06 and IAM-AUTH-07
    // =================================================================================

    /// <summary>
    /// The guidance shown when somebody cannot get in.
    ///
    /// Written to help a real person without confirming anything to somebody probing.
    /// </summary>
    [HttpGet("account-unavailable-and-recovery-guidance")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AccountRecoveryGuidanceResponse>), StatusCodes.Status200OK)]
    public IActionResult GetRecoveryGuidance(
        [FromQuery] string? reason,
        [FromQuery] DateTimeOffset? retryAfterUtc,
        [FromQuery] string? supportEmail) =>
        FromResult(reauthentication.GetRecoveryGuidance(reason, retryAfterUtc, supportEmail, null));


    /// <summary>
    /// Starts recovery from the account-unavailable screen.
    ///
    /// A suspended account is sent a link that lifts the hold and sets a new password in one
    /// step; anybody else gets the ordinary recovery link. Which of the two was sent is
    /// deliberately not reported back.
    /// </summary>
    [HttpPost("account-unavailable-and-recovery-guidance/start-recovery")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ForgotPasswordResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartRecoveryAsync(
        [FromBody] StartRecoveryRequest request, CancellationToken cancellationToken) =>
        FromResult(await passwordRecovery.HandleAsync(
            new StartRecoveryCommand(request), cancellationToken));

    /// <summary>
    /// Sends a message to the service desk from somebody who cannot get in.
    ///
    /// Recorded in the audit trail as well as sent, because "I could not sign in and nobody
    /// replied" is exactly the kind of report that needs a record behind it.
    /// </summary>
    [HttpPost("account-unavailable-and-recovery-guidance/contact-support")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ContactSupportAsync(
        [FromBody] ContactSupportRequest request, CancellationToken cancellationToken) =>
        FromResult(await reauthentication.HandleAsync(
            new ContactSupportCommand(request), cancellationToken));

    /// <summary>
    /// What the step-up screen shows: why it is asking, and how long is left.
    ///
    /// A verification code is asked for only when the account genuinely has a confirmed second
    /// factor - asking for one from somebody with no way to produce it strands them.
    /// </summary>
    [HttpGet("session-timeout-and-reauthentication")]
    [ProducesResponseType(typeof(ApiResponse<ReauthenticationViewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReauthenticationViewAsync(
        [FromQuery] string? protectedActionSummary,
        [FromQuery] string? draftToken,
        CancellationToken cancellationToken) =>
        FromResult(await reauthentication.HandleAsync(
            new GetReauthenticationViewQuery(protectedActionSummary, draftToken), cancellationToken));

    /// <summary>
    /// Parks a half-filled form before sending somebody to confirm their identity.
    ///
    /// WITHOUT THIS, STEP-UP IS A PUNISHMENT. Losing ten minutes of typing teaches people to
    /// avoid the protected screens entirely, which is the opposite of what the control is for.
    /// The payload is form state, never credentials; it is short-lived, single-use, and readable
    /// only by the session that parked it.
    /// </summary>
    [HttpPost("session-timeout-and-reauthentication/save-draft")]
    [ProducesResponseType(typeof(ApiResponse<SaveProtectedDraftResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveProtectedDraftAsync(
        [FromBody] SaveProtectedDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await reauthentication.HandleAsync(
            new CreateProtectedDraftCommand(request.ActionCode, request.TargetId, request.Payload),
            cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : FromResult(Result.Success(new SaveProtectedDraftResponse(
                result.Value!,
                "Your work has been kept. It will be restored once you confirm who you are.")));
    }

    [HttpPost("session-timeout-and-reauthentication")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<ReauthenticateResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReauthenticateAsync(
        [FromBody] ReauthenticateRequest request, CancellationToken cancellationToken) =>
        FromResult(await reauthentication.HandleAsync(
            new ReauthenticateCommand(request), cancellationToken));

    // =================================================================================
    // Host resolution and navigation
    // =================================================================================

    /// <summary>
    /// Which Organisation this host belongs to.
    ///
    /// Called by the client BEFORE the sign-in form is drawn, so it can show the right name
    /// and logo. Anonymous, and returns branding and lifecycle status only — never anything
    /// about who has an account there.
    /// </summary>
    [HttpGet("/api/v1/auth/resolve-tenant")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantResolutionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveTenantAsync(
        [FromQuery] string? host, CancellationToken cancellationToken) =>
        FromResult(await views.HandleAsync(new ResolveTenantQuery(host), cancellationToken));

    /// <summary>
    /// The navigation the caller should render.
    ///
    /// Called once after sign-in and again after every Organisation switch. Everything in it
    /// has already been filtered by permission, so the client renders what it is given.
    /// </summary>
    [HttpGet("/api/v1/auth/navigation")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<NavigationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNavigationAsync(CancellationToken cancellationToken) =>
        FromResult(await navigation.HandleAsync(new GetNavigationQuery(), cancellationToken));

    /// <summary>The signed-in caller, for the client shell.</summary>
    [HttpGet("/api/v1/auth/me")]
    [Authorize]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<SessionStatusResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        FromResult(await tokens.GetSessionStatusAsync(cancellationToken));

    // =================================================================================
    // Shared
    // =================================================================================

    /// <summary>
    /// Writes the refresh token into the HttpOnly cookie and strips it from the body.
    ///
    /// EVERY path that issues tokens goes through here. A refresh token that reached the
    /// response body would be readable by any script on the page, which is exactly what the
    /// cookie exists to prevent — so removing it is not optional tidying, it is the control.
    /// </summary>
    private IActionResult IssueTokens(Result<SignInResponse> result)
    {
        if (result.IsFailure)
        {
            return FromResult(result);
        }

        var response = result.Value!;

        if (!string.IsNullOrWhiteSpace(response.RefreshToken) && response.RefreshTokenExpiresAtUtc.HasValue)
        {
            cookies.Write(Response, response.RefreshToken, response.RefreshTokenExpiresAtUtc.Value);
        }

        return FromResult(Result.Success(StripRefreshToken(response)), response.Message);
    }

    private static SignInResponse StripRefreshToken(SignInResponse response) =>
        response with { RefreshToken = null };
}
