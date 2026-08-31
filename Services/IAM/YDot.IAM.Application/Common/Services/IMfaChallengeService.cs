using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Services;

/// <summary>
/// Issues and verifies one-time codes.
///
/// TWO THINGS MAKE A SIX-DIGIT CODE A REAL FACTOR, and both live here rather than in the
/// callers: the attempt ceiling, and the purpose binding. A code is worthless without the
/// first (a million guesses is nothing to a script) and dangerous without the second (a code
/// mailed to confirm an enrolment must not also authorise a privileged action).
/// </summary>
public interface IMfaChallengeService
{
    /// <summary>
    /// Issues a challenge against the user primary method, or a named one. Retires any
    /// outstanding challenge of the same purpose first, so only the newest code works.
    /// </summary>
    Task<Result<MfaChallengeResponse>> IssueAsync(
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        MfaChallengePurpose purpose,
        Guid? mfaMethodId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies a code against a challenge.
    ///
    /// The purpose is checked as well as the code: a challenge minted for one job cannot
    /// satisfy another, so the weakest flow that can issue a code does not set the strength
    /// of every flow that consumes one.
    /// </summary>
    Task<Result<MfaVerificationResult>> VerifyAsync(
        string challengeToken,
        string code,
        MfaChallengePurpose expectedPurpose,
        CancellationToken cancellationToken);

    /// <summary>Verifies a TOTP code straight against the authenticator secret, with no challenge row.</summary>
    bool VerifyAuthenticatorCode(User user, string code);

    /// <summary>Redeems a single-use backup code and marks it spent.</summary>
    Task<Result<MfaVerificationResult>> RedeemRecoveryCodeAsync(
        string challengeToken, string recoveryCode, CancellationToken cancellationToken);

    /// <summary>Generates a fresh batch of backup codes, retiring the previous one.</summary>
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
        User user, CancellationToken cancellationToken);
}

/// <summary>
/// A verified challenge. Carries the user so the caller does not have to re-load them, and
/// the resolved purpose so it can assert what it just proved.
/// </summary>
public sealed record MfaVerificationResult(
    User User,
    MfaChallenge Challenge,
    MfaChallengePurpose Purpose,
    bool UsedRecoveryCode);
