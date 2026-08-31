using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.DTOs;

// =====================================================================================
// Sign in — IAM-AUTH-01
// =====================================================================================

/// <summary>
/// Sign-in body.
///
/// THERE IS NO TenantId HERE, AND THAT IS THE POINT. The Organisation is resolved from the
/// host the request arrived on (ten1.ngoplanet.com), never from the body. Accepting one here
/// would let anybody authenticate against any Organisation simply by editing the JSON, which
/// is exactly the boundary section 47 of the brief says must never be caller-controlled.
///
/// <paramref name="Identifier"/> is an e-mail or a username — the same field either way, so
/// the person does not have to know which one their administrator gave them.
/// </summary>
public sealed record SignInRequest(
    string Identifier,
    string Password,
    bool RememberMe = false,
    ClientType ClientType = ClientType.Web,
    string? DeviceIdentifier = null,
    string? DeviceName = null,
    string? TrustedDeviceToken = null);

/// <summary>
/// What a sign-in attempt produced.
///
/// One response type covers four genuinely different outcomes, distinguished by
/// <see cref="Status"/>, because the client needs to branch on all four and a 200 with a
/// status beats four different status codes for a flow this stateful:
///
/// <code>
/// Succeeded              -> tokens are present, go to the dashboard
/// MfaRequired            -> ChallengeToken is present, go to the MFA screen
/// TenantSelectionRequired-> SelectableTenants is populated, show the Organisation picker
/// PasswordChangeRequired -> PasswordResetToken is present, force a change
/// </code>
/// </summary>
public sealed record SignInResponse(
    SignInResultStatus Status,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    string TokenType,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc,
    Guid? SessionId,
    AuthenticatedUserResponse? User,
    TenantContextResponse? Tenant,

    /// <summary>Opaque handle for the MFA screen. Never a user id, which would leak existence.</summary>
    string? ChallengeToken,
    string? MfaMaskedDestination,
    MfaMethodType? MfaMethodType,

    /// <summary>Populated only when SuperAdmin has to choose an Organisation.</summary>
    IReadOnlyList<TenantOptionResponse> SelectableTenants,

    /// <summary>Single-use token letting the person set a new password without signing in.</summary>
    string? PasswordResetToken,

    /// <summary>
    /// How many attempts remain before lockout. Surfaced only once it gets low — showing it
    /// from the first failure would hand an attacker a progress bar.
    /// </summary>
    int? AttemptsRemaining,

    int? LockoutMinutesRemaining,
    string? Message);

/// <summary>The four branches a sign-in can take.</summary>
public enum SignInResultStatus
{
    Succeeded = 0,
    MfaRequired = 1,
    TenantSelectionRequired = 2,
    PasswordChangeRequired = 3
}

/// <summary>
/// The signed-in person, as the client shell needs them. Deliberately compact: it is embedded
/// in every sign-in and refresh response, so it carries identity and nothing that belongs on
/// a profile screen.
/// </summary>
public sealed record AuthenticatedUserResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Email,
    string Username,
    string? AvatarUrl,
    UserStatus Status,
    PrivilegeLevel PrivilegeLevel,
    bool IsSuperAdmin,
    bool IsTenantAdmin,
    bool MfaEnabled,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAtUtc,
    string? PreferredCulture,
    string? TimeZone,
    IReadOnlyList<string> Roles,

    /// <summary>
    /// Every permission code the caller holds. The client uses it to hide what somebody
    /// cannot do — a convenience, never the control. The API re-checks on every request,
    /// because anything the browser holds can be edited.
    /// </summary>
    IReadOnlyList<string> Permissions);

/// <summary>
/// Which Organisation the session is operating in, and under what scope.
///
/// <paramref name="IsTenantMode"/> is true when a Global caller has selected an Organisation.
/// The client shows the "acting as" banner off this, so a root user always knows whose data
/// they are looking at — which is the difference between a safe administrative action and an
/// accident.
/// </summary>
public sealed record TenantContextResponse(
    Guid? TenantId,
    string? TenantCode,
    string? TenantName,
    string? Subdomain,
    TenantStatus? Status,
    Guid BusinessUnitId,
    string BusinessUnitCode,
    string BusinessUnitName,
    AccessScopeType Scope,
    bool IsTenantMode,
    string? LogoUrl,
    string TimeZone,
    string DefaultCurrency,
    string DefaultCulture);

/// <summary>One Organisation in the SuperAdmin switcher.</summary>
public sealed record TenantOptionResponse(
    Guid TenantId,
    string Code,
    string Name,
    string Subdomain,
    TenantStatus Status,
    string? LogoUrl,
    bool IsOperable);

// =====================================================================================
// MFA — IAM-AUTH-05
// =====================================================================================

/// <summary>
/// Verifies a one-time code. The challenge is identified by its opaque token rather than by a
/// user id, so the endpoint reveals nothing about which accounts exist.
/// </summary>
public sealed record VerifyMfaRequest(
    string ChallengeToken,
    string Code,

    /// <summary>Remember this browser and stop challenging it for the configured window.</summary>
    bool TrustThisDevice = false,

    string? DeviceName = null,
    string? DeviceIdentifier = null,
    ClientType ClientType = ClientType.Web);

/// <summary>Asks for a fresh code, for example after switching from SMS to e-mail.</summary>
public sealed record ResendMfaChallengeRequest(string ChallengeToken, Guid? MfaMethodId = null);

/// <summary>The challenge as the screen needs to render it.</summary>
public sealed record MfaChallengeResponse(
    string ChallengeToken,
    MfaMethodType MethodType,
    string? MaskedDestination,
    DateTimeOffset ExpiresAtUtc,
    int AttemptsRemaining,
    IReadOnlyList<MfaMethodOptionResponse> AvailableMethods,
    bool RecoveryCodeAccepted,

    /// <summary>
    /// False for an authenticator application, where the code is generated on the device and
    /// nothing was sent anywhere.
    ///
    /// The screen uses this to decide between "open your authenticator app" and "we sent a code
    /// to ...", and — more usefully — whether to offer Resend at all. Offering Resend for an
    /// authenticator invites people to press it and then wonder why no message arrives.
    /// </summary>
    bool CodeWasSent,

    /// <summary>
    /// The sentence to show above the code box, already written.
    ///
    /// It lives on the server because it depends on facts the server holds — which method, which
    /// masked destination, how long the code lasts — and because two clients writing their own
    /// version of it is two chances to describe the same situation differently.
    /// </summary>
    string Instruction);

/// <summary>An alternative factor the person can switch to.</summary>
public sealed record MfaMethodOptionResponse(
    Guid Id,
    MfaMethodType MethodType,
    string? Label,
    string? MaskedDestination,
    bool IsPrimary);

/// <summary>Signing in with a backup code when the second factor is unavailable.</summary>
public sealed record RedeemRecoveryCodeRequest(string ChallengeToken, string RecoveryCode);

/// <summary>
/// Abandoning a half-finished sign-in.
///
/// The challenge is retired immediately rather than left to expire, so a code already sent to a
/// phone or inbox stops working the moment the person says they did not mean to start.
/// </summary>
public sealed record CancelMfaChallengeRequest(string ChallengeToken, string? Reason = null);

// =====================================================================================
// Tokens
// =====================================================================================

/// <summary>
/// Refresh body.
///
/// The token is normally absent: it travels in the HttpOnly cookie, which JavaScript cannot
/// read and therefore cannot leak. The field exists for the mobile client, which has no
/// cookie jar and has to send it explicitly.
/// </summary>
public sealed record RefreshTokenRequest(string? RefreshToken = null);

/// <summary>A rotated token pair.</summary>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    string TokenType,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    Guid SessionId,
    AuthenticatedUserResponse? User = null,
    TenantContextResponse? Tenant = null);

/// <summary>Ends one session, or every session for the user.</summary>
public sealed record SignOutRequest(bool AllDevices = false);

/// <summary>Revokes one specific session from the security screen.</summary>
public sealed record RevokeSessionRequest(Guid SessionId, string? Reason = null);

// =====================================================================================
// SuperAdmin Organisation switching — section 13
// =====================================================================================

/// <summary>
/// SuperAdmin choosing which Organisation to operate in.
///
/// The handler validates that the caller really is Global scope and that the Organisation
/// exists and is reachable. It issues a NEW access token carrying the selected
/// <c>tenant_id</c> — and writes nothing to the SuperAdmin user row. Section 4.1: "Selecting
/// a Tenant must NOT modify SuperAdmin persistent User.TenantId."
/// </summary>
public sealed record SelectTenantRequest(Guid TenantId);

/// <summary>The re-scoped token, plus the Organisation now in force.</summary>
public sealed record SelectTenantResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    string TokenType,
    Guid SessionId,
    TenantContextResponse Tenant,
    AuthenticatedUserResponse User);

// =====================================================================================
// Invitation and activation — IAM-AUTH-02
// =====================================================================================

/// <summary>
/// What the activation screen shows before anybody types a password: which Organisation this
/// invitation is for and who it was addressed to. Deliberately thin — an unauthenticated
/// caller holding a token should learn only what they need to decide whether to proceed.
/// </summary>
public sealed record InvitationPreviewResponse(
    bool IsValid,
    string? Email,
    string? DisplayName,
    string? TenantName,
    string? TenantCode,
    string? BusinessUnitName,
    string? LogoUrl,
    InvitationType InvitationType,
    DateTimeOffset? ExpiresAtUtc,
    bool RequiresOrganisationProfile,
    string? Message,

    /// <summary>
    /// What the invited person is being given: the role, the department, the access window.
    ///
    /// Read-only on the screen, and that is the point. These are the terms an administrator
    /// approved; letting the invited person edit their own role on the way in would defeat the
    /// approval entirely. Showing them still matters, because somebody who was invited to the
    /// wrong job should be able to see that before they accept.
    /// </summary>
    string? Username,
    string? AccountCategory,
    string? Department,
    string? OrganisationUnit,
    string? Designation,
    string? InvitedRoleSummary,
    DateTimeOffset? AccessStartsAtUtc,
    DateTimeOffset? AccessEndsAtUtc,

    /// <summary>
    /// The password rules, so the live checklist beside the password box matches what the
    /// server will actually accept. Duplicating the policy in TypeScript is how a screen ends up
    /// promising a password is fine and then having it rejected on submit.
    /// </summary>
    int PasswordMinimumLength,
    int PasswordMaximumLength,
    bool PasswordRequireUppercase,
    bool PasswordRequireLowercase,
    bool PasswordRequireDigit,
    bool PasswordRequireNonAlphanumeric,

    /// <summary>
    /// True when this Organisation insists on a second factor for this account.
    ///
    /// The screen uses it to decide whether the enrolment step can be skipped. It is a
    /// convenience: activation is refused server-side either way if a required factor is
    /// missing, so a client that ignored this would simply fail at the last step.
    /// </summary>
    bool MfaMandatory,

    /// <summary>The factors this Organisation permits, in the order to offer them.</summary>
    IReadOnlyList<MfaMethodType> AllowedMfaMethods,

    /// <summary>
    /// Dialling prefixes for the mobile number on the SMS/WhatsApp enrolment step.
    ///
    /// THEY TRAVEL WITH THE PREVIEW BECAUSE THE ACTIVATION SCREEN IS ANONYMOUS. It used to
    /// fetch them from <c>/masters/lookups/countries</c>, which is gated on ActiveUserOnly —
    /// a person part-way through activation has no session, so that call answered 401, the
    /// interceptor tried to renew a session that did not exist, and the whole screen was
    /// redirected to sign-in. The catalogue is the right source; an authenticated route to it
    /// is not available here, and this reply already carries the rest of what the screen needs.
    /// </summary>
    IReadOnlyList<string> DialingCodes);

/// <summary>
/// Accepting an invitation: choose a password and activate.
///
/// Acceptance acts on the user id recorded ON THE INVITATION ROW. It never looks the e-mail
/// up globally, because a global lookup is exactly how an invitation meant for TEN001 would
/// activate the unrelated same-address account in TEN002.
/// </summary>
public sealed record AcceptInvitationRequest(
    string Token,
    string Password,
    string ConfirmPassword,
    string? FirstName = null,
    string? LastName = null,
    string? MobileCountryCode = null,
    string? MobileNumber = null,
    bool AcceptTerms = true,
    ClientType ClientType = ClientType.Web,
    string? DeviceIdentifier = null);

/// <summary>
/// The outcome of activation. Signs the person straight in, so they are not asked to type
/// the password they just chose.
///
/// <paramref name="RequiresOrganisationProfile"/> is true for a TenantAdmin whose Organisation
/// still needs its details filled in, and the client routes them to the onboarding wizard
/// rather than the dashboard.
/// </summary>
public sealed record AcceptInvitationResponse(
    bool Succeeded,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    string? RefreshToken,
    Guid? SessionId,
    AuthenticatedUserResponse? User,
    TenantContextResponse? Tenant,
    bool RequiresOrganisationProfile,
    bool RequiresMfaEnrolment,
    string Message,

    /// <summary>
    /// The backup codes, READABLE EXACTLY ONCE — here.
    ///
    /// Only hashes are stored, so there is no second chance to show them and no support process
    /// that can recover them; a lost set is replaced, never retrieved. Empty when the person
    /// activated without enrolling a second factor, because codes that back up nothing would
    /// only be one more secret to look after.
    /// </summary>
    IReadOnlyList<string> RecoveryCodes,

    /// <summary>True when a second factor was enrolled and confirmed during activation.</summary>
    bool MfaEnrolled,

    /// <summary>The warning shown beside the codes. Empty when there are none.</summary>
    string RecoveryCodeNotice);

// -------------------------------------------------------------------------------------
// Enrolling a second factor DURING activation
//
// This is a separate pair of calls from the self-service enrolment on the security page, and
// it has to be: the person has no session yet. They hold an invitation token and nothing else,
// so the token is what authorises these two calls, exactly as it authorises the activation
// itself. The method is created Pending and only becomes usable once a code from it has been
// verified, so nobody can enrol a factor they cannot actually use and lock themselves out on
// their very first sign-in.
// -------------------------------------------------------------------------------------

/// <summary>Starts enrolling a second factor while activating an invited account.</summary>
public sealed record BeginInvitationMfaEnrolmentRequest(
    string Token,
    MfaMethodType MethodType,
    string? MobileCountryCode = null,
    string? MobileNumber = null,
    string? Label = null);

/// <summary>Proves the factor enrolled during activation actually works.</summary>
public sealed record VerifyInvitationMfaEnrolmentRequest(
    string Token,
    Guid MethodId,
    string Code);

/// <summary>
/// What an enrolment panel needs to render a QR code and a typeable secret.
///
/// THIS LIVES IN THE AUTHENTICATION DTOs, NOT UNDER MySecurity, because enrolment happens in two
/// places: the security page of somebody already signed in, and the activation screen of somebody
/// who is not signed in at all. One shape for both keeps the two screens honest about being the
/// same operation, which they are.
/// </summary>
public sealed record MfaEnrolmentResponse(
    Guid MethodId,
    MfaMethodType MethodType,

    /// <summary>Base32, returned EXACTLY ONCE. Never readable again through any endpoint.</summary>
    string? SharedSecret,

    /// <summary>The otpauth:// URI to render as a QR code. Returned once, like the secret.</summary>
    string? ProvisioningUri,

    string? MaskedDestination,
    string Message);

/// <summary>Asks for a replacement invitation. The old link stops working immediately.</summary>
public sealed record RequestNewInvitationRequest(string Token);

/// <summary>
/// Leaves the activation flow without completing it.
///
/// The invitation stays usable on purpose: somebody who backs out to check something with their
/// administrator should be able to return to the same link, and burning it here would generate a
/// support call for what is a normal hesitation.
/// </summary>
public sealed record CancelActivationRequest(string Token, string? Reason = null);

// -------------------------------------------------------------------------------------
// Screen contracts for the recovery and step-up flows.
//
// Each of these is a GET the screen makes BEFORE it draws anything, so it can tell the person
// whether the link they followed is still good instead of letting them type a new password into
// a form that was never going to work. The pattern matters most on the recovery screens, where
// the alternative is somebody carefully choosing a password, pressing Save, and only then being
// told the link expired an hour ago.
// -------------------------------------------------------------------------------------

/// <summary>
/// Whether a recovery link is still usable, and the rules the new password must satisfy.
///
/// Deliberately says NOTHING about whose account it is. Somebody holding a link that has expired
/// has proved nothing, and naming the account would turn a stale link into a disclosure.
/// </summary>
public sealed record ResetPasswordViewResponse(
    bool IsTokenValid,
    DateTimeOffset? TokenExpiresAtUtc,
    int PasswordMinimumLength,
    int PasswordMaximumLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireNonAlphanumeric,
    int PasswordHistoryCount,

    /// <summary>
    /// The warning that every other session will end. Shown before the change, not after, so
    /// nobody is surprised to find themselves signed out on their other machine.
    /// </summary>
    string SessionRevocationNotice,

    string Message);

/// <summary>Asks for a fresh recovery link when the current one has lapsed.</summary>
public sealed record RequestNewRecoveryLinkRequest(string Identifier);

/// <summary>
/// What the step-up screen needs: why it is asking, and how much of the session is left.
///
/// <c>VerificationCodeRequired</c> is true only when the account actually has a second factor
/// enrolled. Asking for a code the person has no way to produce would strand them.
/// </summary>
public sealed record ReauthenticationViewResponse(
    bool IsAuthenticated,
    string? DisplayName,
    string? Email,
    bool VerificationCodeRequired,
    int SecondsUntilSessionEnds,
    string? ProtectedActionSummary,

    /// <summary>Present when work was parked before the timeout, so the screen can restore it.</summary>
    string? DraftToken,

    string UnsavedWorkNotice,
    string Message);

/// <summary>Parks unsaved work before a step-up, so nothing is lost to a timeout.</summary>
public sealed record SaveProtectedDraftRequest(
    string ActionCode,
    string Payload,
    Guid? TargetId = null);

/// <summary>The handle to hand back to the step-up call, which returns the payload with it.</summary>
public sealed record SaveProtectedDraftResponse(string DraftToken, string Message);

/// <summary>
/// Starts recovery from the account-unavailable screen.
///
/// A suspended account is e-mailed a reactivation link that lifts the hold and sets a new
/// password in one step; anybody else gets the ordinary recovery link. Which of the two was sent
/// is deliberately NOT reported back, for the same reason forgot-password says nothing.
/// </summary>
public sealed record StartRecoveryRequest(string Identifier);

/// <summary>A message to the service desk from somebody who cannot get in.</summary>
public sealed record ContactSupportRequest(
    string Message,
    string? ContactEmail = null,
    string? SupportReference = null);

/// <summary>Re-sends an invitation that lapsed or never arrived.</summary>
public sealed record ResendInvitationRequest(Guid UserId, string? Message = null);

// =====================================================================================
// Password recovery — IAM-AUTH-03 and IAM-AUTH-04
// =====================================================================================

/// <summary>
/// Starting a password reset.
///
/// Like sign-in, this carries no Organisation: the host decides which Organisation the
/// address is looked up in, so a reset for TEN001 can never touch the same address in TEN002.
/// </summary>
public sealed record ForgotPasswordRequest(string Identifier);

/// <summary>
/// The reply to a reset request. Deliberately identical whether or not the account exists —
/// a different message for an unknown address is a free way to test which addresses are
/// registered.
/// </summary>
public sealed record ForgotPasswordResponse(string Message, bool EmailSent);

/// <summary>Completing a reset with the token from the e-mail.</summary>
public sealed record ResetPasswordRequest(string Token, string Password, string ConfirmPassword);

/// <summary>Changing a password from inside a session, which requires the current one.</summary>
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword,

    /// <summary>Ends every other session, which is the right default after a change.</summary>
    bool SignOutOtherSessions = true);

/// <summary>Result of any password operation, with the policy so the screen can explain itself.</summary>
public sealed record PasswordOperationResponse(
    bool Succeeded,
    string Message,
    bool RequiresSignIn,
    PasswordPolicyResponse? Policy = null);

/// <summary>
/// The password rules, sent to the client so the strength meter matches what the server will
/// actually accept. Without this the two drift and people are refused for reasons the screen
/// told them were fine.
/// </summary>
public sealed record PasswordPolicyResponse(
    int MinimumLength,
    int MaximumLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireNonAlphanumeric,
    int HistoryCount);

// =====================================================================================
// Re-authentication and session timeout — IAM-AUTH-07
// =====================================================================================

/// <summary>
/// Proving it is still you, before a sensitive action or after an idle timeout. Accepts a
/// password or an MFA code, because the person may have either to hand.
/// </summary>
public sealed record ReauthenticateRequest(
    string? Password = null,
    string? MfaCode = null,

    /// <summary>Resumes a parked sensitive action once the step-up succeeds.</summary>
    string? DraftToken = null);

/// <summary>Result of a step-up, and how long it is good for.</summary>
public sealed record ReauthenticateResponse(
    bool Succeeded,
    string? StepUpToken,
    DateTimeOffset? ValidUntilUtc,
    string? DraftPayload,
    string Message);

/// <summary>
/// The current session as the idle-timeout banner needs it, so the client counts down against
/// the server clock rather than its own.
/// </summary>
public sealed record SessionStatusResponse(
    bool IsAuthenticated,
    Guid? SessionId,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastActivityAtUtc,
    int IdleTimeoutMinutes,
    int SecondsUntilIdleTimeout,
    bool MfaCompleted,
    bool RequiresReauthentication,
    AuthenticatedUserResponse? User,
    TenantContextResponse? Tenant);

// =====================================================================================
// Account unavailable — IAM-AUTH-06
// =====================================================================================

/// <summary>
/// What the "you cannot get in" screen shows.
///
/// The wording is chosen to help a real person without confirming anything to somebody
/// probing: it explains what to do next without saying whether the account exists.
/// </summary>
public sealed record AccountRecoveryGuidanceResponse(
    string Reason,
    string Title,
    string Message,
    IReadOnlyList<string> Steps,
    bool CanSelfUnlock,
    bool CanRequestReset,
    DateTimeOffset? RetryAfterUtc,
    int? MinutesRemaining,
    string? SupportEmail,
    string? SupportPhone);

/// <summary>
/// Which Organisation a host resolves to, called by the client before the sign-in form is
/// drawn so it can show the right name and logo.
///
/// Anonymous, and therefore deliberately thin: it returns branding and status, never anything
/// about who has an account there.
/// </summary>
public sealed record TenantResolutionResponse(
    bool Resolved,
    Guid? TenantId,
    string? TenantCode,
    string? TenantName,
    string? Subdomain,
    TenantStatus? Status,
    bool IsOperable,
    bool IsPlatformHost,
    string? LogoUrl,
    Guid BusinessUnitId,
    string BusinessUnitName,
    string? Message);
