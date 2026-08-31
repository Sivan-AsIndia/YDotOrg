using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One sign-in attempt, successful or not. Append-only.
///
/// This is the row the brief is asking for when it lists "lastloggedin, ipaddress, IMEI No,
/// ClientType, UserAgent, how many attempts remaining". <c>User</c> carries the last-known
/// values for convenience; this table carries the history, which is what actually answers
/// "who has been trying to get into my account".
///
/// WHY <see cref="UserId"/> IS NULLABLE. An attempt against an address that does not exist
/// still has to be recorded — that is precisely the pattern worth seeing — and there is no
/// user to point at. <see cref="AttemptedIdentifier"/> holds what was typed.
///
/// WHY THE TENANT IS NULLABLE TOO. The host may not have resolved to an Organisation at
/// all, which is itself a recordable outcome (<see cref="SignInOutcome.TenantNotResolved"/>).
///
/// THE ROW RECORDS THE TRUTH, THE RESPONSE DOES NOT. <see cref="Outcome"/> distinguishes
/// UnknownAccount from InvalidCredentials, but the API answers both with the same generic
/// message. Telling the caller which one it was is a free account-enumeration oracle.
/// </summary>
public class SignInAttempt : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    /// <summary>Null when the host did not resolve to an Organisation.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Null when the identifier matched no account.</summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The e-mail or username as typed, normalised. Never the password.</summary>
    public string AttemptedIdentifier { get; set; } = string.Empty;

    /// <summary>The host the attempt arrived on, which is what selects the Organisation.</summary>
    public string? HostName { get; set; }

    public SignInOutcome Outcome { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Internal detail for support. Never returned to the caller.</summary>
    public string? FailureDetail { get; set; }

    public DateTimeOffset AttemptedAtUtc { get; set; }

    // ---- Client capture ------------------------------------------------------------------

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public ClientType ClientType { get; set; } = ClientType.Unknown;

    public string? Browser { get; set; }

    public string? OperatingSystem { get; set; }

    /// <summary>Hardware identifier from a mobile client, where one is supplied.</summary>
    public string? DeviceIdentifier { get; set; }

    public string? Location { get; set; }

    // ---- Lockout bookkeeping, so the screen can be honest about what is left ----------------

    /// <summary>The consecutive-failure count immediately after this attempt.</summary>
    public int FailedAttemptCount { get; set; }

    /// <summary>
    /// How many tries the person had left when this attempt finished. Surfaced on the
    /// sign-in screen once it gets low, so a lockout is never a surprise.
    /// </summary>
    public int AttemptsRemaining { get; set; }

    /// <summary>True when this particular attempt is the one that tripped the lockout.</summary>
    public bool TriggeredLockout { get; set; }

    public DateTimeOffset? LockoutEndUtc { get; set; }

    /// <summary>Set when the attempt got as far as needing a second factor.</summary>
    public bool MfaChallenged { get; set; }

    /// <summary>The session opened by a successful attempt, tying the two together.</summary>
    public Guid? SessionId { get; set; }

    public string? CorrelationId { get; set; }
}
