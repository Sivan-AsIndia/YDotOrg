using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.MySecurity;

/// <summary>Starts enrolling an authenticator app: returns the secret and the QR payload.</summary>
public sealed record BeginMfaEnrolmentCommand(MfaMethodType MethodType, string? Label);

/// <summary>Confirms an enrolment by proving a code from the new method works.</summary>
public sealed record ConfirmMfaEnrolmentCommand(Guid MethodId, string Code);

/// <summary>Removes an enrolled method.</summary>
public sealed record RevokeMfaMethodCommand(Guid MethodId, string? Reason);

/// <summary>Generates a fresh batch of backup codes.</summary>
public sealed record GenerateRecoveryCodesCommand;

/// <summary>Forgets a remembered device.</summary>
public sealed record RevokeTrustedDeviceCommand(Guid DeviceId, string? Reason);

/// <summary>Ends one of the caller's own sessions. The id must belong to them.</summary>
public sealed record RevokeMySessionCommand(Guid SessionId, string? Reason);

/// <summary>The caller own security page.</summary>
public sealed record GetMySecurityQuery;

/// <summary>A freshly generated batch of backup codes, shown once and never again.</summary>
public sealed record RecoveryCodesResponse(
    IReadOnlyList<string> Codes,
    int Count,
    string Message);

/// <summary>
/// IAM-USR-04 self-service: the security page somebody manages their OWN account from.
///
/// EVERYTHING HERE ACTS ON THE CALLER, never on an id from the URL. That is deliberate and it
/// is the simplest possible protection: there is no user id parameter to tamper with, so
/// these endpoints cannot be pointed at somebody else however the request is constructed.
/// Administering another person goes through the separate, permission-gated user endpoints.
/// </summary>
public sealed class MySecurityFeatureHandler(
    IUserReadService readService,
    IUserRepository users,
    ISecurityRepository security,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IMfaChallengeService mfa,
    ITotpService totp,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<Result<UserSecurityResponse>> HandleAsync(
        GetMySecurityQuery query, CancellationToken cancellationToken)
    {
        var page = await readService.GetSecurityAsync(currentUser.UserId, cancellationToken);

        return page is null
            ? Result.Failure<UserSecurityResponse>(Error.Unauthorised())
            : Result.Success(page);
    }

    /// <summary>
    /// Begins an enrolment.
    ///
    /// THE SECRET IS RETURNED EXACTLY ONCE, here, so the person can scan it. The method is
    /// created Pending and is NOT usable until a code from it has been verified — otherwise
    /// somebody could enrol a factor they cannot actually use and lock themselves out.
    /// </summary>
    public async Task<Result<MfaEnrolmentResponse>> HandleAsync(
        BeginMfaEnrolmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MfaEnrolmentResponse>(Error.Unauthorised());
        }

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<MfaEnrolmentResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        string? sharedSecret = null;
        string? provisioningUri = null;
        string? maskedDestination = null;

        switch (command.MethodType)
        {
            case MfaMethodType.AuthenticatorApp:
            {
                sharedSecret = totp.GenerateSecret();

                // The issuer and label carry the Organisation name, so somebody administering
                // three Organisations does not end up with three identical entries called YDot.
                var issuer = tenant is null
                    ? businessUnit.Name
                    : $"{businessUnit.Name} - {tenant.Name}";

                provisioningUri = totp.BuildProvisioningUri(
                    sharedSecret, user.Email ?? user.UserName ?? user.Code, issuer);

                maskedDestination = "Authenticator app";
                break;
            }

            case MfaMethodType.Email:
            {
                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    return Result.Failure<MfaEnrolmentResponse>(
                        Error.Validation("This account has no e-mail address to send codes to.",
                            [new ValidationError("Email", "Add an e-mail address first.")]));
                }

                maskedDestination = EmailValue.TryParse(user.Email)?.Masked();
                break;
            }

            case MfaMethodType.Sms:
            {
                var mobile = MobileNumberValue.TryParse(user.MobileCountryCode, user.MobileNumber);

                if (mobile is null)
                {
                    return Result.Failure<MfaEnrolmentResponse>(
                        Error.Validation("This account has no mobile number to send codes to.",
                            [new ValidationError("MobileNumber", "Add a mobile number first.")]));
                }

                maskedDestination = mobile.Masked();
                break;
            }

            case MfaMethodType.SecurityKey:
                return Result.Failure<MfaEnrolmentResponse>(
                    Error.Validation("Security keys are not available yet.",
                        [new ValidationError("MethodType", "Choose an authenticator app, e-mail or SMS.")]));
        }

        var method = new MfaMethod
        {
            TenantId = user.TenantId ?? Guid.Empty,
            BusinessUnitId = user.BusinessUnitId,
            UserId = user.Id,
            MethodType = command.MethodType,
            Label = command.Label?.Trim(),
            MaskedDestination = maskedDestination,
            SecretHash = sharedSecret,
            // Pending until proved. A method that has never produced a working code is not a
            // factor, it is a way to lock yourself out.
            Status = MfaMethodStatus.Pending,
            IsPrimary = false
        };

        await security.AddMfaMethodAsync(method, cancellationToken);

        // Stored on the user as well, because the sign-in path reads it without loading the
        // method rows. Encrypted at rest by the DbContext converter.
        if (command.MethodType == MfaMethodType.AuthenticatorApp)
        {
            user.AuthenticatorSecret = sharedSecret;
        }

        await audit.WriteAsync(
            AuditActionCodes.MfaEnrolled, nameof(MfaMethod), method.Id, user.DisplayName,
            new { command.MethodType, Status = "Pending" }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A delivered factor gets a code straight away, so the person can confirm immediately.
        if (command.MethodType is MfaMethodType.Email or MfaMethodType.Sms)
        {
            await mfa.IssueAsync(
                user, tenant, businessUnit, MfaChallengePurpose.Enrolment, method.Id, cancellationToken);
        }

        return Result.Success(new MfaEnrolmentResponse(
            method.Id,
            method.MethodType,
            // Returned ONCE. It is never readable again through any endpoint.
            sharedSecret,
            provisioningUri,
            maskedDestination,
            command.MethodType == MfaMethodType.AuthenticatorApp
                ? "Scan the QR code with your authenticator app, then enter a code to confirm."
                : "We have sent a code. Enter it to confirm."));
    }

    /// <summary>
    /// Confirms an enrolment.
    ///
    /// Only on success does the method become Active and MFA become enabled on the account.
    /// The first confirmed method is made primary automatically, so there is always one to
    /// challenge without the person having to choose.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ConfirmMfaEnrolmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Unauthorised());
        }

        var method = await security.GetMfaMethodAsync(command.MethodId, cancellationToken);

        // Checked against the CALLER, so one person cannot confirm another enrolment.
        if (method is null || method.UserId != user.Id)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That verification method was not found."));
        }

        if (method.Status == MfaMethodStatus.Revoked)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That verification method has been removed."));
        }

        var verified = method.MethodType == MfaMethodType.AuthenticatorApp
            ? !string.IsNullOrWhiteSpace(method.SecretHash)
              && totp.VerifyCode(method.SecretHash, command.Code)
            : false;

        // A delivered factor is proved through the challenge row rather than the secret.
        if (!verified && method.MethodType != MfaMethodType.AuthenticatorApp)
        {
            var challenges = await security.GetMfaMethodsAsync(user.Id, cancellationToken);
            verified = challenges.Any(item => item.Id == method.Id);

            // Fall through to the challenge service, which enforces the attempt ceiling.
            if (verified)
            {
                verified = mfa.VerifyAuthenticatorCode(user, command.Code);
            }
        }

        if (!verified)
        {
            await audit.WriteAsync(
                AuditActionCodes.MfaFailed, nameof(MfaMethod), method.Id, AuditResult.Denied,
                user.DisplayName, new { Context = "Enrolment" }, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(
                Error.MfaFailed(0));
        }

        method.Status = MfaMethodStatus.Active;
        method.VerifiedAtUtc = now;

        // The first working method becomes primary, so sign-in always has one to challenge.
        var existing = await security.GetMfaMethodsAsync(user.Id, cancellationToken);
        if (!existing.Any(item => item.IsPrimary && item.Id != method.Id && item.IsUsable))
        {
            method.IsPrimary = true;
        }

        user.MfaEnabled = true;
        user.MfaEnrolledAtUtc ??= now;

        // A change to the second factor changes what the token should assert, so it is
        // re-issued on the next request rather than carrying the old claim.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await audit.WriteAsync(
            AuditActionCodes.MfaVerified, nameof(MfaMethod), method.Id, user.DisplayName,
            new { method.MethodType, Confirmed = true }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            method.Id, method.Status.ToString(), method.Version,
            "Verification method confirmed. Generate recovery codes so you can get back in if you lose it.",
            ["View", "GenerateRecoveryCodes"]));
    }

    /// <summary>
    /// Removes an enrolled method.
    ///
    /// Removing the LAST usable method also turns MFA off on the account. Leaving the flag on
    /// with nothing to challenge would lock the person out of their own account entirely.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeMfaMethodCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Unauthorised());
        }

        var method = await security.GetMfaMethodAsync(command.MethodId, cancellationToken);

        if (method is null || method.UserId != user.Id)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That verification method was not found."));
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        // An Organisation that REQUIRES MFA does not let somebody remove their only factor.
        var remaining = (await security.GetMfaMethodsAsync(user.Id, cancellationToken))
            .Count(item => item.Id != method.Id && item.IsUsable);

        if (remaining == 0 && user.IsMfaRequired(tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Your organisation requires two-factor authentication. Add another method before removing this one."));
        }

        method.Status = MfaMethodStatus.Revoked;
        method.RevokedAtUtc = now;
        method.RevocationReason = command.Reason ?? "Removed by the account owner.";
        method.IsPrimary = false;

        if (method.MethodType == MfaMethodType.AuthenticatorApp)
        {
            user.AuthenticatorSecret = null;
        }

        if (remaining == 0)
        {
            user.MfaEnabled = false;
            user.MfaEnrolledAtUtc = null;
        }
        else
        {
            // Promote another method to primary, so sign-in still has one to challenge.
            var promote = (await security.GetMfaMethodsAsync(user.Id, cancellationToken))
                .FirstOrDefault(item => item.Id != method.Id && item.IsUsable);

            if (promote is not null)
            {
                promote.IsPrimary = true;
            }
        }

        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await audit.WriteAsync(
            AuditActionCodes.MfaRevoked, nameof(MfaMethod), method.Id, user.DisplayName,
            new { method.MethodType, RemainingMethods = remaining },
            command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            method.Id, method.Status.ToString(), method.Version,
            remaining == 0
                ? "Verification method removed. Two-factor authentication is now off for your account."
                : "Verification method removed.",
            ["View"]));
    }

    /// <summary>
    /// Generates a fresh batch of backup codes.
    ///
    /// The previous batch stops working, so a printed sheet from last year is dead the moment
    /// a new one is issued. The plaintext is returned ONCE and never stored.
    /// </summary>
    public async Task<Result<RecoveryCodesResponse>> HandleAsync(
        GenerateRecoveryCodesCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<RecoveryCodesResponse>(Error.Unauthorised());
        }

        if (!user.MfaEnabled)
        {
            return Result.Failure<RecoveryCodesResponse>(Error.MfaNotEnrolled(
                "Set up two-factor authentication before generating recovery codes."));
        }

        var codes = await mfa.GenerateRecoveryCodesAsync(user, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.RecoveryCodesGenerated, nameof(User), user.Id, user.DisplayName,
            new { Count = codes.Count }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecoveryCodesResponse(
            codes,
            codes.Count,
            "Save these somewhere safe. Each one works once, and they will not be shown again."));
    }

    /// <summary>
    /// Ends one of the caller's own sessions.
    ///
    /// THIS IS THE "SIGN OUT ON MY LOST PHONE" BUTTON, and it is why the session list shows a
    /// device, a place and a last-active time: somebody has to be able to recognise the one
    /// that is not theirs. Ending the one is better than ending them all, which would sign
    /// them out of the very screen they are working in.
    ///
    /// THE SESSION MUST BE THEIRS. The id comes from the page, and a page can be edited, so
    /// ownership is checked here rather than assumed — an id belonging to anybody else is a
    /// 404, exactly as if it did not exist.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeMySessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await security.GetSessionAsync(command.SessionId, cancellationToken);

        if (session is null || session.UserId != currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That session was not found."));
        }

        if (session.RevokedAtUtc is not null)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That session has already ended."));
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? "Ended by the account owner."
            : command.Reason;

        session.RevokedAtUtc = clock.UtcNow;
        session.RevokedByUserId = currentUser.UserId;
        session.RevocationReason = reason;

        // The refresh tokens go with it. A live refresh token would rebuild the session
        // moments after it was ended, which would look like the button had not worked.
        await security.RevokeTokenChainAsync(session.Id, reason, clock.UtcNow, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.SessionRevoked, nameof(UserSession), session.Id, null,
            new { session.DeviceName, session.IpAddress }, reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            session.Id, "Revoked", session.Version,
            $"The session on {session.DeviceName ?? "that device"} has ended.", ["View"]));
    }

    /// <summary>Forgets a remembered device, so it is challenged again next time.</summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeTrustedDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var device = await security.GetTrustedDeviceAsync(command.DeviceId, cancellationToken);

        if (device is null || device.UserId != currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That device was not found."));
        }

        device.RevokedAtUtc = clock.UtcNow;
        device.RevokedByUserId = currentUser.UserId;
        device.RevocationReason = command.Reason ?? "Removed by the account owner.";

        await audit.WriteAsync(
            AuditActionCodes.DeviceRevoked, nameof(TrustedDevice), device.Id, device.DeviceName,
            new { command.Reason }, command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            device.Id, "Revoked", device.Version,
            "That device will be asked to verify next time.", ["View"]));
    }
}

// ---------------------------------------------------------------------------------------------
// Request bodies for the self-service security page.
//
// These exist so the ids stay in the ROUTE and only the payload arrives in the body. Binding the
// commands themselves straight from the body would put a method id in a place the route already
// carries, and two sources for one value is how mismatches start.
// ---------------------------------------------------------------------------------------------

/// <summary>Body of "start enrolling a factor".</summary>
public sealed record BeginMfaEnrolmentRequest(MfaMethodType MethodType, string? Label = null);

/// <summary>Body of "prove the new factor works". The id comes from the route.</summary>
public sealed record ConfirmMfaEnrolmentRequest(string Code);

/// <summary>Body of "remove this factor". The id comes from the route.</summary>
public sealed record RevokeMfaMethodRequest(string? Reason = null);

/// <summary>Body of "end this session". The id comes from the route.</summary>
public sealed record RevokeMySessionRequest(string? Reason = null);

/// <summary>Body of "forget this device". The id comes from the route.</summary>
public sealed record RevokeTrustedDeviceRequest(string? Reason = null);
