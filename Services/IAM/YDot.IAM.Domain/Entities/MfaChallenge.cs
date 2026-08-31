using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One outstanding one-time code.
///
/// PURPOSE-BOUND. <see cref="Purpose"/> is checked on verification, so a code e-mailed to
/// confirm an enrolment cannot be replayed to satisfy a sign-in or to authorise a
/// privileged action. Without that check, the weakest flow that can issue a code would set
/// the strength of every flow that consumes one.
///
/// ATTEMPT-LIMITED. A six-digit code is guessable in a million tries, which is nothing to a
/// script. <see cref="AttemptCount"/> against <see cref="MaximumAttempts"/> is what turns it
/// back into a real factor.
/// </summary>
public class MfaChallenge : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Null when the challenge is not tied to a specific enrolled method.</summary>
    public Guid? MfaMethodId { get; set; }

    public MfaMethod? MfaMethod { get; set; }

    public MfaMethodType MethodType { get; set; }

    public MfaChallengePurpose Purpose { get; set; }

    /// <summary>SHA-256 of the code. Never the digits themselves.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// The opaque handle the client echoes back with the code. Lets the verification call
    /// find the challenge without the client having to hold a user id, which would leak
    /// whether an account exists.
    /// </summary>
    public string ChallengeToken { get; set; } = string.Empty;

    public string? MaskedDestination { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public int MaximumAttempts { get; set; } = 5;

    public bool IsConsumed { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>True when the code may still be presented for verification.</summary>
    public bool IsVerifiable(DateTimeOffset asOf) =>
        !IsConsumed && VerifiedAtUtc is null && ExpiresAtUtc > asOf && AttemptCount < MaximumAttempts;

    public int AttemptsRemaining => Math.Max(0, MaximumAttempts - AttemptCount);
}
