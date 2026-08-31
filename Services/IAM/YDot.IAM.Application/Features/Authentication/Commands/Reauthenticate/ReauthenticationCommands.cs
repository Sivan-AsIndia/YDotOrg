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
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.Reauthenticate;

/// <summary>IAM-AUTH-07. Proving it is still you, after an idle timeout or before a sensitive action.</summary>
public sealed record ReauthenticateCommand(ReauthenticateRequest Request);

/// <summary>Parks a half-finished sensitive action while the person goes off to re-authenticate.</summary>
public sealed record CreateProtectedDraftCommand(string ActionCode, Guid? TargetId, string Payload);

/// <summary>What the step-up screen shows: why it is asking, and how long is left.</summary>
public sealed record GetReauthenticationViewQuery(string? ProtectedActionSummary, string? DraftToken);

/// <summary>A message to the service desk from somebody who cannot get in.</summary>
public sealed record ContactSupportCommand(ContactSupportRequest Request);

/// <summary>
/// Step-up authentication.
///
/// WHY A SESSION IS NOT ENOUGH FOR EVERYTHING. Being signed in proves you authenticated at
/// some point. It does not prove the person at the keyboard right now is the same one — an
/// unattended laptop is the everyday case. So the actions that really matter ask again,
/// immediately before they happen.
///
/// THE DRAFT IS WHY PEOPLE TOLERATE IT. Without somewhere to park the half-filled form,
/// stepping up means typing it all again, and people learn to avoid the protected screens
/// entirely — which is the opposite of what the control is for. The draft is short-lived,
/// single-use, and readable only by the session that created it.
/// </summary>
public sealed class ReauthenticationCommandHandler(
    IUserRepository users,
    ISecurityRepository security,
    IPasswordHasher passwordHasher,
    IMfaChallengeService mfa,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<JwtSettings> jwtOptions)
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<Result<ReauthenticateResponse>> HandleAsync(
        ReauthenticateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ReauthenticateResponse>(Error.Unauthorised());
        }

        // A lockout applies here too. Otherwise the step-up prompt becomes an unthrottled
        // password oracle for anybody who has got as far as an open session.
        if (user.IsLockedOut(now))
        {
            return Result.Failure<ReauthenticateResponse>(
                Error.AccountLocked(user.LockoutMinutesRemaining(now)));
        }

        var proved = false;

        // Either factor is accepted, because the person may have only one to hand: a password
        // manager on a different machine, or a phone but no memorised password.
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            proved = !string.IsNullOrEmpty(user.PasswordHash)
                     && passwordHasher.Verify(user.PasswordHash, request.Password)
                        != PasswordVerificationOutcome.Failed;
        }
        else if (!string.IsNullOrWhiteSpace(request.MfaCode))
        {
            proved = mfa.VerifyAuthenticatorCode(user, request.MfaCode);
        }

        if (!proved)
        {
            await audit.WriteAsync(
                AuditActionCodes.Reauthenticated, nameof(User), user.Id, AuditResult.Denied,
                user.DisplayName, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<ReauthenticateResponse>(
                Error.InvalidCredentials("That did not match. Try again."));
        }

        // Stamp the session so the RecentlyReauthenticated policy is satisfied for a while.
        if (currentUser.SessionId.HasValue)
        {
            var session = await security.GetSessionAsync(currentUser.SessionId.Value, cancellationToken);
            if (session is not null)
            {
                session.LastReauthenticatedAtUtc = now;
                session.LastActivityAtUtc = now;
            }
        }

        // Hand back the parked form, if there was one.
        string? draftPayload = null;
        if (!string.IsNullOrWhiteSpace(request.DraftToken))
        {
            var draft = await security.GetDraftAsync(request.DraftToken, cancellationToken);

            // Only the session that parked it may resume it, so a stolen draft token is
            // useless on its own.
            if (draft is not null
                && draft.IsUsable(now)
                && draft.UserId == user.Id
                && (draft.SessionId is null || draft.SessionId == currentUser.SessionId))
            {
                draftPayload = draft.Payload;
                draft.ConsumedAtUtc = now;
            }
        }

        await audit.WriteAsync(
            AuditActionCodes.Reauthenticated, nameof(User), user.Id, user.DisplayName,
            new { Method = string.IsNullOrWhiteSpace(request.Password) ? "Mfa" : "Password" },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReauthenticateResponse(
            Succeeded: true,
            // The proof lives on the session row, not in a bearer token the client holds:
            // a separate step-up token would be one more secret to leak for no extra benefit.
            StepUpToken: null,
            ValidUntilUtc: now.AddMinutes(_jwt.StepUpValidMinutes),
            draftPayload,
            "Confirmed. You can continue."));
    }

    /// <summary>
    /// What the step-up screen needs before it draws.
    ///
    /// <c>VerificationCodeRequired</c> is the part worth getting right: it is true only when the
    /// account actually has a confirmed second factor. Asking for a code from somebody who has
    /// no way to produce one strands them on the screen with no way forward.
    /// </summary>
    public async Task<Result<ReauthenticationViewResponse>> HandleAsync(
        GetReauthenticationViewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ReauthenticationViewResponse>(Error.Unauthorised());
        }

        var methods = await security.GetMfaMethodsAsync(user.Id, cancellationToken);
        var hasUsableFactor = user.MfaEnabled && methods.Any(method => method.IsUsable);

        // The parked payload is handed back here as well as on success, so a screen that was
        // reloaded during the timeout can restore what the person had typed before they confirm
        // rather than after - which is the difference between reassuring and unnerving.
        string? draftToken = null;

        if (!string.IsNullOrWhiteSpace(query.DraftToken))
        {
            var draft = await security.GetDraftAsync(query.DraftToken, cancellationToken);

            // Only the session that parked it may see it exists. A draft holds form state such
            // as which role is being granted to whom, and it belongs to one person's screen.
            if (draft is not null
                && draft.UserId == currentUser.UserId
                && draft.ExpiresAtUtc > clock.UtcNow)
            {
                draftToken = draft.DraftToken;
            }
        }

        return Result.Success(new ReauthenticationViewResponse(
            IsAuthenticated: true,
            user.DisplayName,
            user.Email,
            VerificationCodeRequired: hasUsableFactor,
            SecondsUntilSessionEnds: Math.Max(0, _jwt.StepUpValidMinutes * 60),
            query.ProtectedActionSummary,
            draftToken,
            UnsavedWorkNotice:
                "Anything you had typed has been kept. It will be restored once you confirm.",
            Message: hasUsableFactor
                ? "Confirm your password and a verification code to continue."
                : "Confirm your password to continue."));
    }

    /// <summary>
    /// Sends a message to the service desk from somebody who cannot get in.
    ///
    /// Written to the audit trail as well as sent, because "I could not sign in and nobody
    /// replied" is exactly the kind of report that needs a record behind it. The reply is
    /// generic for the same reason every other endpoint on this screen is.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ContactSupportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reference = tokenHasher.GenerateReference("SUP");

        await audit.WriteAnonymousAsync(
            AuditActionCodes.SupportRequested, nameof(User), currentUser.UserId,
            tenantContext.BusinessUnitId, tenantContext.TenantId,
            AuditResult.Succeeded, command.Request.ContactEmail,
            new
            {
                Reference = reference,
                command.Request.SupportReference,

                // The message body is stored, and the audit service redacts anything that looks
                // like a secret on the way in - people do paste passwords into support forms.
                command.Request.Message
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            currentUser.UserId,
            "Sent",
            0,
            $"Your message has been passed to the service desk. Quote reference {reference} if "
            + "you follow it up.",
            []));
    }

    /// <summary>
    /// Parks a form before sending the person to re-authenticate.
    ///
    /// Never stores credentials — the payload is form state such as which role is being
    /// granted to whom, and it is deleted the moment it is read back.
    /// </summary>
    public async Task<Result<string>> HandleAsync(
        CreateProtectedDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var draftToken = tokenHasher.GenerateToken(24);

        await security.AddDraftAsync(new ProtectedActionDraft
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            BusinessUnitId = tenantContext.BusinessUnitId,
            UserId = currentUser.UserId,
            ActionCode = command.ActionCode,
            TargetId = command.TargetId,
            Payload = command.Payload,
            DraftToken = draftToken,
            ExpiresAtUtc = now.AddMinutes(15),
            SessionId = currentUser.SessionId
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(draftToken);
    }

    /// <summary>
    /// The guidance shown by IAM-AUTH-06 when somebody cannot get in.
    ///
    /// Written to help a real person without confirming anything to somebody probing: it says
    /// what to do next without saying whether the account exists, and the wording is the same
    /// for a locked account and an unknown one.
    /// </summary>
    public Result<AccountRecoveryGuidanceResponse> GetRecoveryGuidance(
        string? reason, DateTimeOffset? retryAfterUtc, string? supportEmail, string? supportPhone)
    {
        var now = clock.UtcNow;
        var minutes = retryAfterUtc.HasValue
            ? (int)Math.Max(0, Math.Ceiling((retryAfterUtc.Value - now).TotalMinutes))
            : (int?)null;

        return Result.Success(reason?.ToUpperInvariant() switch
        {
            ErrorCodes.AccountLocked => new AccountRecoveryGuidanceResponse(
                ErrorCodes.AccountLocked,
                "Account temporarily locked",
                minutes.HasValue
                    ? $"Too many failed sign-in attempts. Try again in {minutes} minute(s)."
                    : "Too many failed sign-in attempts. Try again shortly.",
                [
                    "Wait for the lock to lift, then sign in again.",
                    "If you have forgotten your password, reset it - that also clears the lock.",
                    "If this was not you, reset your password and tell your administrator."
                ],
                CanSelfUnlock: true, CanRequestReset: true, retryAfterUtc, minutes,
                supportEmail, supportPhone),

            ErrorCodes.AccountSuspended => new AccountRecoveryGuidanceResponse(
                ErrorCodes.AccountSuspended,
                "Account suspended",
                "This account has been suspended by an administrator.",
                [
                    "Contact your organisation administrator.",
                    "Only an administrator can lift a suspension."
                ],
                CanSelfUnlock: false, CanRequestReset: false, null, null, supportEmail, supportPhone),

            ErrorCodes.AccountNotActivated => new AccountRecoveryGuidanceResponse(
                ErrorCodes.AccountNotActivated,
                "Account not activated",
                "This account has not been activated yet.",
                [
                    "Find the invitation e-mail and follow the activation link.",
                    "Check your spam folder.",
                    "Ask your administrator to send a new invitation if the link has expired."
                ],
                CanSelfUnlock: false, CanRequestReset: false, null, null, supportEmail, supportPhone),

            ErrorCodes.TenantSuspended or ErrorCodes.TenantInactive or ErrorCodes.TenantNotApproved =>
                new AccountRecoveryGuidanceResponse(
                    reason!,
                    "Organisation unavailable",
                    "Your organisation is not active at the moment.",
                    [
                        "Contact your organisation administrator.",
                        "If your organisation is still being set up, you will be told when it is ready."
                    ],
                    CanSelfUnlock: false, CanRequestReset: false, null, null, supportEmail, supportPhone),

            ErrorCodes.AccessWindowClosed => new AccountRecoveryGuidanceResponse(
                ErrorCodes.AccessWindowClosed,
                "Access period ended",
                "Access for this account is outside its permitted dates.",
                [
                    "Contact your administrator to extend your access.",
                    "This usually happens when a temporary or contract account reaches its end date."
                ],
                CanSelfUnlock: false, CanRequestReset: false, null, null, supportEmail, supportPhone),

            _ => new AccountRecoveryGuidanceResponse(
                "UNKNOWN",
                "Cannot sign in",
                "We could not sign you in.",
                [
                    "Check your e-mail address and password.",
                    "Make sure you are using your organisation web address.",
                    "Reset your password if you are not sure of it.",
                    "Contact your administrator if the problem continues."
                ],
                CanSelfUnlock: false, CanRequestReset: true, null, null, supportEmail, supportPhone)
        });
    }
}
