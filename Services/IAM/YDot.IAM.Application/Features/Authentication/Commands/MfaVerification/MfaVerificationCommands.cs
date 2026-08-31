using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Authentication.Commands.SignIn;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.MfaVerification;

/// <summary>IAM-AUTH-05. Completes a sign-in by verifying the second factor.</summary>
public sealed record VerifyMfaCommand(VerifyMfaRequest Request);

/// <summary>Issues a fresh code, for example after switching from SMS to e-mail.</summary>
public sealed record ResendMfaChallengeCommand(ResendMfaChallengeRequest Request);

/// <summary>Signs in with a single-use backup code when the second factor is unavailable.</summary>
public sealed record RedeemRecoveryCodeCommand(RedeemRecoveryCodeRequest Request);

/// <summary>Abandons a half-finished sign-in and retires the outstanding challenge.</summary>
public sealed record CancelMfaChallengeCommand(CancelMfaChallengeRequest Request);

/// <summary>
/// The MFA half of the sign-in flow.
///
/// WHY THIS SHARES <c>SignInCommandHandler.CompleteSignInAsync</c>. Once the code checks out,
/// everything that has to happen is identical to a sign-in that never needed a second factor:
/// resolve access, open a session, mint tokens, stamp the capture columns, record the attempt,
/// write the audit row. Duplicating that here would eventually drift, and the copy that
/// drifted would be the one that forgot to record the session — which is the one that then
/// cannot be revoked.
/// </summary>
public sealed class MfaVerificationCommandHandler(
    IMfaChallengeService mfa,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ISecurityRepository security,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IUserAgentParser userAgents,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    SignInCommandHandler signIn,
    IOptions<SecuritySettings> securityOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<Result<SignInResponse>> HandleAsync(
        VerifyMfaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var verification = await mfa.VerifyAsync(
            request.ChallengeToken, request.Code, MfaChallengePurpose.SignIn, cancellationToken);

        if (verification.IsFailure)
        {
            await audit.WriteAnonymousAsync(
                AuditActionCodes.MfaFailed, nameof(User), null,
                Guid.Empty, null, AuditResult.Denied,
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SignInResponse>(verification.Error!);
        }

        return await CompleteAsync(verification.Value!, request.TrustThisDevice,
            request.DeviceName, request.DeviceIdentifier, request.ClientType, cancellationToken);
    }

    public async Task<Result<SignInResponse>> HandleAsync(
        RedeemRecoveryCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var verification = await mfa.RedeemRecoveryCodeAsync(
            command.Request.ChallengeToken, command.Request.RecoveryCode, cancellationToken);

        if (verification.IsFailure)
        {
            return Result.Failure<SignInResponse>(verification.Error!);
        }

        await audit.WriteAsync(
            AuditActionCodes.RecoveryCodeRedeemed, nameof(User), verification.Value!.User.Id,
            verification.Value.User.DisplayName, cancellationToken: cancellationToken);

        // A recovery code never trusts the device. The person is here BECAUSE their normal
        // factor is unavailable, so the one thing not to do is quietly lower the bar for next
        // time on whatever machine they happen to be using.
        return await CompleteAsync(
            verification.Value, trustDevice: false, null, null, ClientType.Web, cancellationToken);
    }

    /// <summary>
    /// Abandons a half-finished sign-in.
    ///
    /// The challenge is consumed rather than left to expire, so a code already sitting in an
    /// inbox or on a phone stops working the moment the person says they did not mean to start.
    /// Leaving it live for the rest of its window would mean "cancel" quietly did nothing.
    ///
    /// AN UNKNOWN TOKEN IS REPORTED AS SUCCESS. There is nothing to cancel and nothing went
    /// wrong, and answering differently would turn this into a way of testing whether a
    /// challenge token is real.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelMfaChallengeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var challenge = await security.GetChallengeAsync(command.Request.ChallengeToken, cancellationToken);

        if (challenge is not null && !challenge.IsConsumed)
        {
            challenge.IsConsumed = true;

            if (challenge.User is not null)
            {
                await audit.WriteAsync(
                    AuditActionCodes.MfaChallengeCancelled, nameof(User), challenge.UserId,
                    challenge.User.DisplayName,
                    new { challenge.Purpose, command.Request.Reason },
                    cancellationToken: cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            challenge?.UserId ?? Guid.Empty,
            "Cancelled",
            0,
            "The sign-in was cancelled.",
            []));
    }

    public async Task<Result<MfaChallengeResponse>> HandleAsync(
        ResendMfaChallengeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var challenge = await security.GetChallengeAsync(command.Request.ChallengeToken, cancellationToken);
        if (challenge is null)
        {
            return Result.Failure<MfaChallengeResponse>(Error.TokenInvalid("That verification session has ended."));
        }

        var user = challenge.User;
        if (user is null)
        {
            return Result.Failure<MfaChallengeResponse>(Error.TokenInvalid("That verification session has ended."));
        }

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<MfaChallengeResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = challenge.TenantId == Guid.Empty
            ? null
            : await tenants.GetByIdAsync(challenge.TenantId, cancellationToken);

        var reissued = await mfa.IssueAsync(
            user, tenant, businessUnit, challenge.Purpose, command.Request.MfaMethodId, cancellationToken);

        if (reissued.IsSuccess)
        {
            await audit.WriteAsync(
                AuditActionCodes.MfaChallengeIssued, nameof(User), user.Id, user.DisplayName,
                new { challenge.Purpose, Resent = true }, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return reissued;
    }

    /// <summary>
    /// Shared tail: mark the session MFA-complete, optionally remember the device, and hand
    /// over to the sign-in handler for everything else.
    /// </summary>
    private async Task<Result<SignInResponse>> CompleteAsync(
        MfaVerificationResult verification,
        bool trustDevice,
        string? deviceName,
        string? deviceIdentifier,
        ClientType clientType,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = verification.User;

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<SignInResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        if (trustDevice)
        {
            await TrustDeviceAsync(user, deviceName, deviceIdentifier, clientType, now, cancellationToken);
        }

        await audit.WriteAsync(
            AuditActionCodes.MfaVerified, nameof(User), user.Id, user.DisplayName,
            new { verification.Purpose, verification.UsedRecoveryCode },
            cancellationToken: cancellationToken);

        var client = userAgents.Parse(currentUser.UserAgent, clientType.ToString());

        var request = new SignInRequest(
            user.Email ?? string.Empty,
            string.Empty,
            RememberMe: false,
            clientType,
            deviceIdentifier,
            deviceName);

        return await signIn.CompleteSignInAsync(
            user, tenant, businessUnit, request, client,
            user.NormalizedEmail ?? string.Empty, now,
            usedTrustedDevice: trustDevice, cancellationToken);
    }

    /// <summary>
    /// Remembers this browser so ordinary sign-in stops asking for a code.
    ///
    /// Only the hash of the device token is stored; the plaintext goes into an HttpOnly
    /// cookie. The trust expires on its own, so a machine trusted a year ago is challenged
    /// again without anybody having to remember to revoke it.
    /// </summary>
    private async Task TrustDeviceAsync(
        User user, string? deviceName, string? deviceIdentifier, ClientType clientType,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var client = userAgents.Parse(currentUser.UserAgent, clientType.ToString());
        var token = tokenHasher.GenerateToken();

        await security.AddTrustedDeviceAsync(new TrustedDevice
        {
            TenantId = user.TenantId ?? Guid.Empty,
            BusinessUnitId = user.BusinessUnitId,
            UserId = user.Id,
            DeviceTokenHash = tokenHasher.Hash(token),
            DeviceName = deviceName ?? client.DeviceName,
            DeviceIdentifier = deviceIdentifier,
            ClientType = clientType,
            UserAgent = currentUser.UserAgent,
            Browser = client.Browser,
            OperatingSystem = client.OperatingSystem,
            IpAddress = currentUser.IpAddress,
            TrustedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_security.TrustedDeviceDays),
            LastSeenAtUtc = now
        }, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.DeviceTrusted, nameof(TrustedDevice), null,
            deviceName ?? client.DeviceName, cancellationToken: cancellationToken);

        // The plaintext is handed back through the response header so the API layer can put
        // it in an HttpOnly cookie. It is never persisted and never returned in the body.
        TrustedDeviceTokenAccessor.Set(token);
    }
}

/// <summary>
/// Carries the freshly minted trusted-device token from the handler out to the API layer,
/// which writes it into an HttpOnly cookie.
///
/// An AsyncLocal rather than a return value because the token is a side effect of ONE branch
/// of a flow whose response type is already shared with three other branches. Threading an
/// optional token through <c>SignInResponse</c> would put a secret in the response body of
/// every sign-in, which is precisely where it must not be — JavaScript can read a body, and
/// the whole point of the HttpOnly cookie is that it cannot.
/// </summary>
public static class TrustedDeviceTokenAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public static void Set(string? token) => Current.Value = token;

    /// <summary>Reads and clears, so a token cannot leak into a later request on the same thread.</summary>
    public static string? Take()
    {
        var value = Current.Value;
        Current.Value = null;
        return value;
    }
}
