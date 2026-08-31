namespace YDot.IAM.Application.Common.Results;

/// <summary>
/// A failure description. <see cref="Code"/> is the stable catalogue code,
/// <see cref="StatusCode"/> is the HTTP status the API returns, and <see cref="Errors"/>
/// carries the field-level messages for a validation failure.
///
/// One factory per row of the error catalogue, so no handler ever invents a code and the
/// Angular client can switch on a closed set of strings.
/// </summary>
public sealed record Error(string Code, string Message, int StatusCode, IReadOnlyList<ValidationError>? Errors = null)
{
    // ---- Section 11 catalogue -------------------------------------------------------------

    public static Error Validation(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.ValidationFailed, message, 400, errors);

    public static Error Unauthorised(string message = "Authentication is required.") =>
        new(ErrorCodes.AuthenticationRequired, message, 401);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new(ErrorCodes.PermissionDenied, message, 403);

    public static Error UserNotFound(string message = "That user was not found inside your scope.") =>
        new(ErrorCodes.UserNotFound, message, 404);

    public static Error NotFound(string message = "The record was not found inside your scope.") =>
        new(ErrorCodes.RecordNotFound, message, 404);

    public static Error Duplicate(string message) =>
        new(ErrorCodes.DuplicateRecord, message, 409);

    public static Error InvalidTransition(string message) =>
        new(ErrorCodes.InvalidStatusTransition, message, 409);

    public static Error Concurrency(string message =
        "This record changed after you opened it. Review the latest version before continuing.") =>
        new(ErrorCodes.ConcurrencyConflict, message, 409);

    /// <summary>
    /// Something the request depends on is unavailable or misconfigured: no BusinessUnit, an
    /// unreachable mail server. 502, because the CALLER did nothing wrong and retrying the same
    /// request later may well work.
    /// </summary>
    public static Error Dependency(string message) =>
        new(ErrorCodes.DependencyFailure, message, 502);

    /// <summary>
    /// The record is still referenced by something, so it cannot be removed: a department with
    /// people in it, a role somebody holds, a unit with offices beneath it.
    ///
    /// 409 rather than the 502 above, and the distinction matters: this is not a fault, it is
    /// the system refusing to orphan data. The caller can act on it — move the people, then try
    /// again — and a 502 would tell them to wait for something to recover instead.
    /// </summary>
    public static Error InUse(string message) =>
        new(ErrorCodes.RecordInUse, message, 409);

    // ---- Authentication ----------------------------------------------------------------------

    /// <summary>
    /// The single answer for a wrong password AND for an address that does not exist. The
    /// wording is identical on purpose: any difference between the two, including a
    /// difference in response time, tells an attacker which addresses are real.
    /// </summary>
    public static Error InvalidCredentials(string message = "The sign-in details are incorrect.") =>
        new(ErrorCodes.InvalidCredentials, message, 401);

    public static Error AccountLocked(int minutesRemaining) =>
        new(ErrorCodes.AccountLocked,
            $"Too many failed attempts. This account is locked for another {minutesRemaining} minute(s).",
            423);

    /// <summary>
    /// The address is being throttled, not the account.
    ///
    /// 429 rather than 423: nothing is locked and nothing needs an administrator. The caller
    /// simply has to wait.
    /// </summary>
    public static Error TooManyAttempts(string message =
        "Too many sign-in attempts from this address. Wait a moment and try again.") =>
        new(ErrorCodes.TooManyAttempts, message, 429);

    public static Error AccountSuspended(string message = "This account is suspended. Contact your administrator.") =>
        new(ErrorCodes.AccountSuspended, message, 403);

    public static Error AccountDeactivated(string message = "This account is no longer active.") =>
        new(ErrorCodes.AccountDeactivated, message, 403);

    public static Error AccountNotActivated(string message =
        "This account has not been activated yet. Use the link in your invitation e-mail.") =>
        new(ErrorCodes.AccountNotActivated, message, 403);

    public static Error AccessWindowClosed(string message =
        "Access for this account is outside its permitted dates.") =>
        new(ErrorCodes.AccessWindowClosed, message, 403);

    public static Error PasswordChangeRequired(string message = "You must change your password before continuing.") =>
        new(ErrorCodes.PasswordChangeRequired, message, 403);

    public static Error MfaRequired(string message = "A verification code is required.") =>
        new(ErrorCodes.MfaRequired, message, 401);

    public static Error MfaFailed(int attemptsRemaining) =>
        new(ErrorCodes.MfaFailed,
            attemptsRemaining > 0
                ? $"That code is not correct. {attemptsRemaining} attempt(s) remaining."
                : "That code is not correct and no attempts remain. Request a new code.",
            401);

    public static Error MfaNotEnrolled(string message = "No verification method is enrolled for this account.") =>
        new(ErrorCodes.MfaNotEnrolled, message, 400);

    public static Error ReauthenticationRequired(string message =
        "Confirm it is you before continuing with this action.") =>
        new(ErrorCodes.ReauthenticationRequired, message, 401);

    public static Error SessionExpired(string message = "Your session has ended. Sign in again.") =>
        new(ErrorCodes.SessionExpired, message, 401);

    public static Error TokenInvalid(string message = "That link is not valid.") =>
        new(ErrorCodes.TokenInvalid, message, 400);

    public static Error TokenExpired(string message = "That link has expired. Request a new one.") =>
        new(ErrorCodes.TokenExpired, message, 400);

    /// <summary>
    /// A refresh token that had already been spent was presented again. Two parties hold the
    /// same token, so the whole session is destroyed rather than the request simply refused.
    /// </summary>
    public static Error TokenReuseDetected(string message =
        "This session has been ended for security reasons. Sign in again.") =>
        new(ErrorCodes.TokenReuseDetected, message, 401);

    public static Error InvitationInvalid(string message = "That invitation link is not valid.") =>
        new(ErrorCodes.InvitationInvalid, message, 400);

    public static Error InvitationExpired(string message =
        "That invitation has expired. Ask your administrator to send a new one.") =>
        new(ErrorCodes.InvitationExpired, message, 400);

    public static Error InvitationAlreadyAccepted(string message =
        "That invitation has already been used. Sign in instead.") =>
        new(ErrorCodes.InvitationAlreadyAccepted, message, 409);

    public static Error WeakPassword(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.WeakPassword, message, 400, errors);

    public static Error PasswordReused(string message =
        "That password has been used before. Choose one you have not used.") =>
        new(ErrorCodes.PasswordReused, message, 400);

    // ---- Tenancy -------------------------------------------------------------------------------

    /// <summary>The host name did not map to any Organisation.</summary>
    public static Error TenantNotResolved(string message =
        "This address is not linked to an organisation.") =>
        new(ErrorCodes.TenantNotResolved, message, 400);

    public static Error TenantNotFound(string message = "That organisation was not found.") =>
        new(ErrorCodes.TenantNotFound, message, 404);

    public static Error TenantInactive(string message =
        "This organisation is not active yet. Contact your administrator.") =>
        new(ErrorCodes.TenantInactive, message, 403);

    public static Error TenantSuspended(string message =
        "This organisation has been suspended. Contact support.") =>
        new(ErrorCodes.TenantSuspended, message, 403);

    public static Error TenantNotApproved(string message =
        "This organisation is still awaiting approval.") =>
        new(ErrorCodes.TenantNotApproved, message, 403);

    /// <summary>
    /// SuperAdmin authenticated but has not chosen which Organisation to work in. The client
    /// reads this and shows the Organisation selector.
    /// </summary>
    public static Error TenantSelectionRequired(string message =
        "Select an organisation to continue.") =>
        new(ErrorCodes.TenantSelectionRequired, message, 409);

    /// <summary>
    /// The caller tried to touch a record belonging to a different Organisation. This should
    /// be unreachable — the query filter makes such a record invisible rather than forbidden
    /// — so returning it at all means a deliberate cross-Tenant attempt worth alerting on.
    /// </summary>
    public static Error CrossTenantAccessDenied(string message =
        "That record belongs to a different organisation.") =>
        new(ErrorCodes.CrossTenantAccessDenied, message, 403);

    public static Error SubdomainUnavailable(string message =
        "That address is already taken. Choose another.") =>
        new(ErrorCodes.SubdomainUnavailable, message, 409);

    public static Error SubdomainReserved(string message =
        "That address is reserved by the platform. Choose another.") =>
        new(ErrorCodes.SubdomainReserved, message, 400);

    public static Error TenantLimitReached(string message =
        "This business unit has reached its limit on organisations.") =>
        new(ErrorCodes.TenantLimitReached, message, 409);

    public static Error UserLimitReached(string message =
        "This organisation has reached its limit on users.") =>
        new(ErrorCodes.UserLimitReached, message, 409);

    public static Error SuperAdminOnly(string message =
        "Only a platform administrator can do that.") =>
        new(ErrorCodes.SuperAdminOnly, message, 403);

    public static Error ProfileIncomplete(string message =
        "Complete the organisation profile before submitting it for approval.",
        IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.ProfileIncomplete, message, 400, errors);

    public static Error SegregationOfDuties(string message) =>
        new(ErrorCodes.SegregationOfDutiesViolation, message, 409);
}
