namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The result of one sign-in attempt. Every value is recorded against the attempt row, so
/// "why can I not get in?" is answerable from data rather than guesswork.
///
/// Note that the API never tells the caller which of these happened for an unknown account
/// or a wrong password — both surface as the same generic message — but the row still
/// records the truth for the audit trail and the lockout counter.
/// </summary>
public enum SignInOutcome
{
    Succeeded = 0,
    InvalidCredentials = 1,
    UnknownAccount = 2,
    LockedOut = 3,
    Suspended = 4,
    Deactivated = 5,
    Expired = 6,
    MfaRequired = 7,
    MfaFailed = 8,

    /// <summary>Credentials were right but the Organisation is not Active.</summary>
    TenantInactive = 9,

    /// <summary>Credentials were right but the host name did not resolve to a Tenant.</summary>
    TenantNotResolved = 10,

    /// <summary>The account exists in another Tenant, but not in the one being signed in to.</summary>
    WrongTenant = 11,

    /// <summary>Account is real but has never accepted its invitation.</summary>
    NotActivated = 12
}
