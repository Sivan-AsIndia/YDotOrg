using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Issues and verifies one-time codes.
///
/// FOUR THINGS TURN A SIX-DIGIT NUMBER INTO A REAL FACTOR, and all four live here rather than
/// in the callers:
///
///   1. AN ATTEMPT CEILING. A million guesses is nothing to a script; five is a factor.
///   2. PURPOSE BINDING. A code mailed to confirm an enrolment must not also authorise a
///      privileged action, or the weakest flow that can issue a code sets the strength of
///      every flow that consumes one.
///   3. A SHORT EXPIRY, so an intercepted code is worth very little.
///   4. SINGLE USE. Consumed on the first successful verification, whatever happens next.
/// </summary>
public sealed class MfaChallengeService(
    ISecurityRepository security,
    ITokenHasher tokenHasher,
    ITotpService totp,
    INotificationService notifications,
    IDateTimeProvider clock,
    IOptions<SecuritySettings> securityOptions) : IMfaChallengeService
{
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<Result<MfaChallengeResponse>> IssueAsync(
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        MfaChallengePurpose purpose,
        Guid? mfaMethodId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = clock.UtcNow;

        var method = mfaMethodId.HasValue
            ? await security.GetMfaMethodAsync(mfaMethodId.Value, cancellationToken)
            : await security.GetPrimaryMfaMethodAsync(user.Id, cancellationToken);

        // No enrolled method, but the account has an authenticator secret: fall back to a TOTP
        // challenge, which needs no delivery at all.
        var methodType = method?.MethodType
                         ?? (string.IsNullOrWhiteSpace(user.AuthenticatorSecret)
                             ? MfaMethodType.Email
                             : MfaMethodType.AuthenticatorApp);

        if (method is null && methodType == MfaMethodType.Email && string.IsNullOrWhiteSpace(user.Email))
        {
            return Result.Failure<MfaChallengeResponse>(Error.MfaNotEnrolled());
        }

        // Only the newest code works. A mailbox holding three valid codes means the oldest -
        // possibly the intercepted one - still opens the door.
        await security.ConsumeOpenChallengesAsync(user.Id, purpose, now, cancellationToken);

        var challengeToken = tokenHasher.GenerateToken(24);
        var expiresAt = now.AddMinutes(_security.MfaChallengeExpiryMinutes);

        // An authenticator code is computed by the phone from the shared secret, so there is
        // nothing to generate or send. Only the delivered factors get a code.
        string? plaintextCode = null;

        if (methodType != MfaMethodType.AuthenticatorApp)
        {
            plaintextCode = tokenHasher.GenerateNumericCode(_security.TotpDigits);
        }

        var maskedDestination = method?.MaskedDestination ?? BuildMaskedDestination(user, methodType);

        var challenge = new MfaChallenge
        {
            TenantId = user.TenantId ?? tenant?.Id ?? Guid.Empty,
            BusinessUnitId = businessUnit.Id,
            UserId = user.Id,
            MfaMethodId = method?.Id,
            MethodType = methodType,
            Purpose = purpose,
            // For an authenticator challenge there is no stored code - verification goes
            // straight to the TOTP algorithm - so the column holds a placeholder hash.
            CodeHash = plaintextCode is null
                ? tokenHasher.Hash(challengeToken)
                : tokenHasher.Hash(plaintextCode),
            ChallengeToken = challengeToken,
            MaskedDestination = maskedDestination,
            IssuedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            MaximumAttempts = _security.MfaMaximumAttempts
        };

        await security.AddChallengeAsync(challenge, cancellationToken);

        if (plaintextCode is not null)
        {
            await notifications.SendMfaCodeAsync(
                user, tenant, businessUnit, plaintextCode, expiresAt, cancellationToken);
        }

        var available = await security.GetMfaMethodsAsync(user.Id, cancellationToken);

        return Result.Success(new MfaChallengeResponse(
            challengeToken,
            methodType,
            maskedDestination,
            expiresAt,
            challenge.AttemptsRemaining,
            [
                .. available
                    .Where(item => item.IsUsable)
                    .Select(item => new MfaMethodOptionResponse(
                        item.Id, item.MethodType, item.Label, item.MaskedDestination, item.IsPrimary))
            ],
            // Backup codes are accepted only for sign-in. Allowing one to satisfy a step-up
            // would let a single stolen sheet of codes authorise privileged actions.
            RecoveryCodeAccepted: purpose == MfaChallengePurpose.SignIn
                                  && user.RecoveryCodesRemaining > 0,
            CodeWasSent: plaintextCode is not null,
            Instruction: BuildInstruction(methodType, maskedDestination, expiresAt, now)));
    }

    public async Task<Result<MfaVerificationResult>> VerifyAsync(
        string challengeToken,
        string code,
        MfaChallengePurpose expectedPurpose,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var challenge = await security.GetChallengeAsync(challengeToken, cancellationToken);

        if (challenge is null)
        {
            return Result.Failure<MfaVerificationResult>(
                Error.TokenInvalid("That verification session has ended. Sign in again."));
        }

        // THE PURPOSE CHECK. A challenge minted for one job cannot satisfy another, however
        // valid the code itself is.
        if (challenge.Purpose != expectedPurpose)
        {
            return Result.Failure<MfaVerificationResult>(
                Error.TokenInvalid("That verification session is not valid for this action."));
        }

        if (!challenge.IsVerifiable(now))
        {
            return Result.Failure<MfaVerificationResult>(
                challenge.AttemptCount >= challenge.MaximumAttempts
                    ? Error.MfaFailed(0)
                    : Error.TokenExpired("That code has expired. Request a new one."));
        }

        var user = challenge.User;
        if (user is null)
        {
            return Result.Failure<MfaVerificationResult>(Error.TokenInvalid());
        }

        // The attempt is counted BEFORE the comparison, so a failure that throws or a
        // connection that drops still costs the caller an attempt.
        challenge.AttemptCount += 1;

        var verified = challenge.MethodType == MfaMethodType.AuthenticatorApp
            ? VerifyAuthenticatorCode(user, code)
            : tokenHasher.Verify(code.Trim(), challenge.CodeHash);

        if (!verified)
        {
            return Result.Failure<MfaVerificationResult>(Error.MfaFailed(challenge.AttemptsRemaining));
        }

        challenge.VerifiedAtUtc = now;
        challenge.IsConsumed = true;

        if (challenge.MfaMethod is not null)
        {
            challenge.MfaMethod.LastUsedAtUtc = now;

            // A method proves itself by being used successfully for the first time.
            challenge.MfaMethod.VerifiedAtUtc ??= now;

            if (challenge.MfaMethod.Status == MfaMethodStatus.Pending)
            {
                challenge.MfaMethod.Status = MfaMethodStatus.Active;
            }
        }

        return Result.Success(new MfaVerificationResult(
            user, challenge, challenge.Purpose, UsedRecoveryCode: false));
    }

    /// <summary>
    /// Verifies a TOTP code straight against the stored secret, with no challenge row.
    ///
    /// Used by the step-up flow, where the person types a code from their authenticator and
    /// there is nothing to deliver and therefore nothing to track.
    /// </summary>
    public bool VerifyAuthenticatorCode(User user, string code)
    {
        ArgumentNullException.ThrowIfNull(user);

        return !string.IsNullOrWhiteSpace(user.AuthenticatorSecret)
               && totp.VerifyCode(user.AuthenticatorSecret, code);
    }

    /// <summary>
    /// Redeems a single-use backup code.
    ///
    /// The code is marked spent whatever happens next, and the remaining count on the user is
    /// decremented so the security screen can tell them honestly how many are left.
    /// </summary>
    public async Task<Result<MfaVerificationResult>> RedeemRecoveryCodeAsync(
        string challengeToken, string recoveryCode, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var challenge = await security.GetChallengeAsync(challengeToken, cancellationToken);

        if (challenge is null)
        {
            return Result.Failure<MfaVerificationResult>(
                Error.TokenInvalid("That verification session has ended. Sign in again."));
        }

        // Backup codes are for getting back IN, not for authorising something sensitive.
        if (challenge.Purpose != MfaChallengePurpose.SignIn)
        {
            return Result.Failure<MfaVerificationResult>(
                Error.TokenInvalid("A recovery code cannot be used for this action."));
        }

        if (!challenge.IsVerifiable(now))
        {
            return Result.Failure<MfaVerificationResult>(Error.MfaFailed(0));
        }

        var user = challenge.User;
        if (user is null)
        {
            return Result.Failure<MfaVerificationResult>(Error.TokenInvalid());
        }

        challenge.AttemptCount += 1;

        var normalised = recoveryCode.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        var stored = await security.FindRedeemableRecoveryCodeAsync(
            user.Id, tokenHasher.Hash(normalised), cancellationToken);

        if (stored is null)
        {
            return Result.Failure<MfaVerificationResult>(Error.MfaFailed(challenge.AttemptsRemaining));
        }

        stored.RedeemedAtUtc = now;
        user.RecoveryCodesRemaining = Math.Max(0, user.RecoveryCodesRemaining - 1);

        challenge.VerifiedAtUtc = now;
        challenge.IsConsumed = true;

        return Result.Success(new MfaVerificationResult(
            user, challenge, challenge.Purpose, UsedRecoveryCode: true));
    }

    /// <summary>
    /// Generates a fresh batch of backup codes.
    ///
    /// The previous batch is retired first, so a printed sheet from last year stops working
    /// the moment a new one is issued. The plaintext is returned ONCE, to be shown to the
    /// person; only hashes are stored.
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
        User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;

        await security.RetireRecoveryCodesAsync(user.Id, now, cancellationToken);

        var batchId = Guid.NewGuid();
        var plaintext = new List<string>(_security.RecoveryCodeCount);
        var entities = new List<RecoveryCode>(_security.RecoveryCodeCount);

        for (var index = 0; index < _security.RecoveryCodeCount; index++)
        {
            // Grouped as XXXXX-XXXXX for readability, and stored without the hyphen so a
            // person who types it either way is accepted.
            var raw = tokenHasher.GenerateToken(8)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();

            var trimmed = raw.Length >= 10 ? raw[..10] : raw.PadRight(10, 'X');
            var display = $"{trimmed[..5]}-{trimmed[5..]}";

            plaintext.Add(display);

            entities.Add(new RecoveryCode
            {
                TenantId = user.TenantId ?? Guid.Empty,
                BusinessUnitId = user.BusinessUnitId,
                UserId = user.Id,
                CodeHash = tokenHasher.Hash(trimmed),
                BatchId = batchId,
                GeneratedAtUtc = now
            });
        }

        await security.AddRecoveryCodesAsync(entities, cancellationToken);

        user.RecoveryCodesRemaining = entities.Count;

        return plaintext;
    }

    /// <summary>
    /// The masked destination shown on the challenge screen: recognisable to its owner,
    /// useless to anybody else who reaches the screen.
    /// </summary>
    /// <summary>
    /// The sentence shown above the code box.
    ///
    /// Written here rather than in the client because it depends on server-held facts — the
    /// method, the masked destination, how long the code lasts — and because two clients each
    /// writing their own version is two chances to describe the same situation differently.
    /// </summary>
    private static string BuildInstruction(
        MfaMethodType methodType,
        string? maskedDestination,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - now).TotalMinutes));

        return methodType switch
        {
            MfaMethodType.AuthenticatorApp =>
                "Open your authenticator application and enter the current code for this account.",

            MfaMethodType.Email => string.IsNullOrWhiteSpace(maskedDestination)
                ? $"We have e-mailed you a code. It expires in {minutes} minute(s)."
                : $"We have e-mailed a code to {maskedDestination}. It expires in {minutes} minute(s).",

            MfaMethodType.Sms => string.IsNullOrWhiteSpace(maskedDestination)
                ? $"We have sent you a code by text message. It expires in {minutes} minute(s)."
                : $"We have sent a code by text message to {maskedDestination}. "
                  + $"It expires in {minutes} minute(s).",

            MfaMethodType.SecurityKey =>
                "Insert your security key and follow the prompt from your browser.",

            _ => $"Enter the verification code for this account. It expires in {minutes} minute(s)."
        };
    }

    private static string? BuildMaskedDestination(User user, MfaMethodType methodType) => methodType switch
    {
        MfaMethodType.Email => EmailValue.TryParse(user.Email)?.Masked(),
        MfaMethodType.Sms => MobileNumberValue.TryParse(user.MobileCountryCode, user.MobileNumber)?.Masked(),
        MfaMethodType.AuthenticatorApp => "Authenticator app",
        MfaMethodType.SecurityKey => "Security key",
        _ => null
    };
}
