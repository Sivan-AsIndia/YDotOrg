namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Platform-wide security defaults.
///
/// AN ORGANISATION CAN TIGHTEN THESE, NOT LOOSEN THEM. Several of these values are mirrored
/// on <c>Tenant</c> — lockout threshold, lockout duration, password length, idle timeout —
/// and the Tenant value wins where it is stricter. The platform floor stays here so a
/// misconfigured Organisation cannot drop below it.
/// </summary>
public sealed class SecuritySettings
{
    public const string SectionName = "SecuritySettings";

    // ---- Lockout. The brief asks for 5 failures and a 15-minute lockout. ------------------

    public int MaximumFailedAccessAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Warn the person once they are this close to a lockout. Showing "2 attempts remaining"
    /// turns a surprise lockout into a fair warning; showing it from the first failure would
    /// hand an attacker a progress bar.
    /// </summary>
    public int WarnWhenAttemptsRemaining { get; set; } = 2;

    // ---- Passwords -------------------------------------------------------------------------

    public int PasswordMinimumLength { get; set; } = 10;

    public int PasswordMaximumLength { get; set; } = 128;

    public bool PasswordRequireUppercase { get; set; } = true;

    public bool PasswordRequireLowercase { get; set; } = true;

    public bool PasswordRequireDigit { get; set; } = true;

    public bool PasswordRequireNonAlphanumeric { get; set; } = true;

    /// <summary>How many previous passwords are remembered and refused. Zero disables the check.</summary>
    public int PasswordHistoryCount { get; set; } = 5;

    /// <summary>Zero disables expiry, which is the current guidance for most organisations.</summary>
    public int PasswordExpiryDays { get; set; }

    // ---- Sessions ----------------------------------------------------------------------------

    public int SessionIdleTimeoutMinutes { get; set; } = 30;

    public int SessionAbsoluteTimeoutHours { get; set; } = 12;

    /// <summary>
    /// Ceiling on concurrent sessions per user. When exceeded the oldest is revoked, so an
    /// account cannot accumulate a hundred live sessions across abandoned devices.
    /// </summary>
    public int MaximumConcurrentSessions { get; set; } = 10;

    // ---- Tokens and links ----------------------------------------------------------------------

    public int InvitationExpiryDays { get; set; } = 7;

    public int PasswordResetExpiryMinutes { get; set; } = 60;

    public int EmailConfirmationExpiryHours { get; set; } = 24;

    public int MfaChallengeExpiryMinutes { get; set; } = 5;

    public int MfaMaximumAttempts { get; set; } = 5;

    public int RecoveryCodeCount { get; set; } = 10;

    public int TrustedDeviceDays { get; set; } = 30;

    // ---- MFA -------------------------------------------------------------------------------------

    /// <summary>Digits in a TOTP code.</summary>
    public int TotpDigits { get; set; } = 6;

    /// <summary>Seconds a TOTP code is valid for.</summary>
    public int TotpPeriodSeconds { get; set; } = 30;

    /// <summary>
    /// How many periods either side of now are accepted, to tolerate a phone whose clock
    /// drifts. One step each way is the usual compromise between forgiving and loose.
    /// </summary>
    public int TotpAllowedDriftSteps { get; set; } = 1;

    // ---- Rate limiting ------------------------------------------------------------------------------

    /// <summary>Sign-in attempts allowed from one IP per minute, across all accounts.</summary>
    public int SignInAttemptsPerMinutePerIp { get; set; } = 20;

    /// <summary>Password-reset requests allowed per address per hour.</summary>
    public int PasswordResetRequestsPerHour { get; set; } = 5;

    // ---- Behaviour ------------------------------------------------------------------------------------

    /// <summary>
    /// When true (the default and the correct setting), the API answers "unknown address" and
    /// "wrong password" identically. Turning it off makes support easier and hands anybody who
    /// asks a way to test which addresses are registered.
    /// </summary>
    public bool MaskAccountExistence { get; set; } = true;

    /// <summary>Requires a step-up before any permission marked sensitive is exercised.</summary>
    public bool RequireStepUpForSensitiveActions { get; set; } = true;

    /// <summary>
    /// Encryption key for the TOTP secrets at rest, supplied from the environment. When empty
    /// the secrets are stored as-is, which is acceptable in development and not otherwise.
    /// </summary>
    public string? SecretEncryptionKey { get; set; }
}
