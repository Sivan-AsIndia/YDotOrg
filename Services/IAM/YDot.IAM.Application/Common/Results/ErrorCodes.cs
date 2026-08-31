namespace YDot.IAM.Application.Common.Results;

/// <summary>
/// The stable error codes from section 11 of the IAM contract, plus the tenancy codes this
/// build adds. The Angular client switches on these strings, so they must never change once
/// they are published.
/// </summary>
public static class ErrorCodes
{
    // ---- Section 11 catalogue ---------------------------------------------------------
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string RecordNotFound = "RECORD_NOT_FOUND";
    public const string DuplicateRecord = "DUPLICATE_RECORD";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";

    /// <summary>The record is still referenced by something, so it cannot be removed.</summary>
    public const string RecordInUse = "RECORD_IN_USE";

    // ---- Authentication ------------------------------------------------------------------
    // Deliberately vague on purpose. InvalidCredentials is returned for a wrong password AND
    // for an address that does not exist, because distinguishing them hands an attacker a
    // free account-enumeration oracle. The SignInAttempt row still records which it was.
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";

    /// <summary>
    /// Too many sign-in attempts from one address in a short window.
    ///
    /// DISTINCT FROM <see cref="AccountLocked"/> AND THAT MATTERS. Rate limiting used to answer
    /// ACCOUNT_LOCKED, so a person who simply signed in too quickly was told their account was
    /// locked. They then asked an administrator to unlock it, who found the account Active with
    /// zero failed attempts and no lockout - a support conversation about a state that never
    /// existed. This says what actually happened: wait a moment and try again.
    /// </summary>
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string AccountDeactivated = "ACCOUNT_DEACTIVATED";
    public const string AccountNotActivated = "ACCOUNT_NOT_ACTIVATED";
    public const string AccessWindowClosed = "ACCESS_WINDOW_CLOSED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string MfaFailed = "MFA_FAILED";
    public const string MfaNotEnrolled = "MFA_NOT_ENROLLED";
    public const string ReauthenticationRequired = "REAUTHENTICATION_REQUIRED";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string TokenReuseDetected = "TOKEN_REUSE_DETECTED";
    public const string InvitationInvalid = "INVITATION_INVALID";
    public const string InvitationExpired = "INVITATION_EXPIRED";
    public const string InvitationAlreadyAccepted = "INVITATION_ALREADY_ACCEPTED";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string PasswordReused = "PASSWORD_REUSED";

    // ---- Tenancy -----------------------------------------------------------------------------
    // These are the codes the Angular client uses to decide between "sign in again",
    // "pick an Organisation" and "this host is not one of ours".
    public const string TenantNotResolved = "TENANT_NOT_RESOLVED";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string TenantInactive = "TENANT_INACTIVE";
    public const string TenantSuspended = "TENANT_SUSPENDED";
    public const string TenantNotApproved = "TENANT_NOT_APPROVED";
    public const string TenantSelectionRequired = "TENANT_SELECTION_REQUIRED";
    public const string CrossTenantAccessDenied = "CROSS_TENANT_ACCESS_DENIED";
    public const string SubdomainUnavailable = "SUBDOMAIN_UNAVAILABLE";
    public const string SubdomainReserved = "SUBDOMAIN_RESERVED";
    public const string TenantLimitReached = "TENANT_LIMIT_REACHED";
    public const string UserLimitReached = "USER_LIMIT_REACHED";
    public const string SuperAdminOnly = "SUPER_ADMIN_ONLY";
    public const string ProfileIncomplete = "PROFILE_INCOMPLETE";
    public const string SegregationOfDutiesViolation = "SEGREGATION_OF_DUTIES_VIOLATION";
}
