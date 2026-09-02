using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.Commands.MfaVerification;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.SignIn;

/// <summary>IAM-AUTH-01. The Organisation comes from the host, never from this command.</summary>
public sealed record SignInCommand(SignInRequest Request);

/// <summary>
/// The sign-in pipeline.
///
/// THE ORDER OF THE CHECKS IS THE SECURITY DESIGN, not an accident of how it was written:
///
/// <code>
///  1. resolve the Organisation from the host      (who are we authenticating against?)
///  2. rate-limit by IP                            (before any lookup, so probing is cheap for us)
///  3. find the user INSIDE that Organisation      (never globally)
///  4. lockout check                               (before the password, so a locked account
///                                                  cannot be used as a password oracle)
///  5. verify the password
///  6. account state checks
///  7. Organisation state checks
///  8. MFA, if required
///  9. SuperAdmin Organisation selection, if needed
/// 10. issue the session
/// </code>
///
/// TWO RULES RUN THROUGH ALL OF IT.
///
/// First, EVERY FAILURE THAT COULD REVEAL WHETHER AN ACCOUNT EXISTS RETURNS THE SAME ERROR.
/// An unknown address and a wrong password both answer INVALID_CREDENTIALS with identical
/// wording. The <c>SignInAttempt</c> row still records which it really was, so support can
/// see the truth without the caller being told it.
///
/// Second, EVERY ATTEMPT IS RECORDED — success or failure, known account or not, with IP,
/// user agent, client type and device identifier. That is the capture the brief asks for, and
/// it is what makes "who has been trying to get into my account?" answerable from data.
/// </summary>
public sealed class SignInCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ISecurityRepository security,
    IPasswordHasher passwordHasher,
    ISessionTokenService sessions,
    IMfaChallengeService mfa,
    ITokenHasher tokenHasher,
    IEffectiveAccessService effectiveAccess,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserAgentParser userAgents,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;

    public async Task<Result<SignInResponse>> HandleAsync(
        SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;
        var identifier = (request.Identifier ?? string.Empty).Trim().ToLowerInvariant();
        var client = userAgents.Parse(currentUser.UserAgent, request.ClientType.ToString());

        // ---- 1. Which Organisation are we authenticating against? --------------------------
        //
        // Resolved by the middleware from the host. A platform host gives null, which is the
        // SuperAdmin path; an Organisation host gives that Organisation. An unrecognised host
        // gives neither, and there is nothing sensible to authenticate against.
        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<SignInResponse>(
                Error.Dependency("The platform is not configured. Contact support."));
        }

        Tenant? tenant = null;
        if (tenantContext.TenantId.HasValue)
        {
            tenant = await tenants.GetByIdAsync(tenantContext.TenantId.Value, cancellationToken);
        }

        if (tenant is null && !tenantContext.IsPlatformHost)
        {
            await RecordAttemptAsync(
                null, identifier, businessUnit.Id, null, SignInOutcome.TenantNotResolved,
                client, request, now, 0, 0, false, cancellationToken);

            return Result.Failure<SignInResponse>(Error.TenantNotResolved());
        }

        // ---- 2. Rate limit by address, before any database lookup for the account ------------
        if (!string.IsNullOrWhiteSpace(currentUser.IpAddress))
        {
            var recentFromIp = await security.CountRecentAttemptsByIpAsync(
                currentUser.IpAddress, now.AddMinutes(-1), cancellationToken);

            if (recentFromIp >= _security.SignInAttemptsPerMinutePerIp)
            {
                // NOT AccountLocked. Nothing about the account has changed - this is the ADDRESS
                // being throttled, and telling somebody their account is locked sends them to an
                // administrator who will find it perfectly healthy.
                return Result.Failure<SignInResponse>(Error.TooManyAttempts());
            }
        }

        // ---- 3. Find the user INSIDE this Organisation ----------------------------------------
        //
        // On a platform host we look for the global root account only; on an Organisation host
        // we look inside that Organisation only. Neither path can reach the other, which is
        // what stops an Organisation user signing in at the platform door or vice versa.
        var user = tenant is null
            ? await users.FindSuperAdminAsync(identifier, cancellationToken)
            : await users.FindForSignInAsync(identifier, tenant.Id, cancellationToken);

        if (user is null)
        {
            await RecordAttemptAsync(
                null, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.UnknownAccount,
                client, request, now, 0, 0, false, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Same error, same wording, as a wrong password. Deliberately.
            return Result.Failure<SignInResponse>(Error.InvalidCredentials());
        }

        // ---- 4. Lockout, BEFORE the password is checked ------------------------------------------
        //
        // Checking the password first would let somebody use a locked account as an oracle:
        // a different response for right-password-but-locked tells them the password is right.
        if (user.IsLockedOut(now))
        {
            var minutes = user.LockoutMinutesRemaining(now);

            await RecordAttemptAsync(
                user, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.LockedOut,
                client, request, now, user.AccessFailedCount, 0, false, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SignInResponse>(
                Error.AccountLocked(minutes > 0 ? minutes : _security.LockoutMinutes));
        }

        // ---- 5. The password --------------------------------------------------------------------
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            // Invited but never activated. Told plainly, because it is not a secret that an
            // invitation is outstanding and the remedy is in the person own mailbox.
            await RecordAttemptAsync(
                user, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.NotActivated,
                client, request, now, user.AccessFailedCount, 0, false, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SignInResponse>(Error.AccountNotActivated());
        }

        var verification = passwordHasher.Verify(user.PasswordHash, request.Password ?? string.Empty);

        if (verification == PasswordVerificationOutcome.Failed)
        {
            return await HandleFailedPasswordAsync(
                user, tenant, businessUnit, identifier, client, request, now, cancellationToken);
        }

        // Correct. Upgrade the stored hash if it used older parameters, so an account
        // silently strengthens the next time its owner signs in.
        if (verification == PasswordVerificationOutcome.SucceededRehashNeeded)
        {
            user.PasswordHash = passwordHasher.Hash(request.Password!);
        }

        // ---- 6. Account state -------------------------------------------------------------------
        var accountFailure = EvaluateAccountState(user, now);
        if (accountFailure is not null)
        {
            await RecordAttemptAsync(
                user, identifier, businessUnit.Id, tenant?.Id, accountFailure.Value.Outcome,
                client, request, now, user.AccessFailedCount, 0, false, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SignInResponse>(accountFailure.Value.Error);
        }

        // The password was right, so the failure counter resets whatever happens next.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        // ---- 7. Organisation state ----------------------------------------------------------------
        //
        // Kept separate from the account checks on purpose: "your Organisation is suspended"
        // and "your account is suspended" send the person to two different people for help.
        if (tenant is not null && !user.IsSuperAdmin)
        {
            var tenantFailure = EvaluateTenantState(tenant, user);
            if (tenantFailure is not null)
            {
                await RecordAttemptAsync(
                    user, identifier, businessUnit.Id, tenant.Id, SignInOutcome.TenantInactive,
                    client, request, now, 0, 0, false, cancellationToken);

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Failure<SignInResponse>(tenantFailure);
            }
        }

        // ---- 8. Second factor ----------------------------------------------------------------------
        var mfaRequired = user.IsMfaRequired(tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional);

        // Resolved ONCE, here, for every path below rather than only inside the MFA branch. Two
        // things downstream need it: whether the second factor can be skipped, and whether the
        // session row should be stamped as coming from a remembered device. That second one used
        // to be passed as a bare `true` at step 10, so EVERY ordinary sign-in was recorded as
        // having come from a trusted device whether one existed or not.
        var knownDevice = await ResolveTrustedDeviceAsync(
            user, request.TrustedDeviceToken, now, cancellationToken);

        // A device the person previously marked trusted skips the prompt for ordinary sign-in.
        // It is a convenience only: a sensitive action still triggers a step-up.
        //
        // AND ONLY IF IT WAS TRUSTED AFTER THE FACTOR EXISTED. Remembering a browser is offered
        // on the password form too, where no second factor has been proved — so a device
        // remembered before enrolment must not be able to walk past the factor enrolled
        // afterwards. Re-enrolling clears MfaEnrolledAtUtc and sets it again, which correctly
        // retires the older trust as well.
        var maySkipMfa = knownDevice is not null
            && (user.MfaEnrolledAtUtc is null || knownDevice.TrustedAtUtc >= user.MfaEnrolledAtUtc);

        if (mfaRequired && user.MfaEnabled)
        {
            if (!maySkipMfa)
            {
                var challenge = await mfa.IssueAsync(
                    user, tenant, businessUnit, MfaChallengePurpose.SignIn, null, cancellationToken);

                if (challenge.IsFailure)
                {
                    return Result.Failure<SignInResponse>(challenge.Error!);
                }

                await RecordAttemptAsync(
                    user, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.MfaRequired,
                    client, request, now, 0, 0, true, cancellationToken);

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(AuthenticationMappingConfig.ToMfaPendingResponse(challenge.Value!));
            }
        }

        // ---- 8b. "Remember this device" ------------------------------------------------------------
        //
        // THE TICK-BOX ON THE SIGN-IN FORM NOW DOES SOMETHING. It set `RememberMe`, which only
        // ever lengthened the refresh token, so somebody who ticked "Remember this device" and
        // then opened their security page found Remembered Devices reading zero — the list was
        // written by the MFA screen alone, and an account with no second factor never reaches it.
        //
        // Placed here rather than inside either branch below because both of them end a
        // successful sign-in: a root user picking an Organisation is as signed in as anybody else.
        if (request.RememberMe)
        {
            await RememberDeviceAsync(user, knownDevice, request, client, now, cancellationToken);
        }

        // ---- 9. SuperAdmin has to pick an Organisation ------------------------------------------------
        //
        // A root user signing in at the platform host is authenticated but has no operating
        // Organisation yet. They get a Global-scope token that can do exactly two things:
        // list Organisations and select one.
        if (user.IsSuperAdmin && tenant is null)
        {
            var selectable = await tenants.GetSelectableAsync(businessUnit.Id, cancellationToken);

            var access = await effectiveAccess.ResolveAsync(user, null, cancellationToken);

            var tokens = await sessions.IssueAsync(
                user, null, businessUnit, AccessScopeType.Global, request.ClientType,
                mfaCompleted: !mfaRequired || user.MfaEnabled, request.RememberMe,
                tenantContext.HostName, request.DeviceIdentifier, request.DeviceName ?? client.DeviceName,
                isTrustedDevice: knownDevice is not null, cancellationToken);

            ApplyLoginCapture(user, client, request, now);

            await RecordAttemptAsync(
                user, identifier, businessUnit.Id, null, SignInOutcome.Succeeded,
                client, request, now, 0, 0, false, cancellationToken, tokens.SessionId);

            await audit.WriteAsync(
                AuditActionCodes.SignInSucceeded, nameof(User), user.Id, user.DisplayName,
                new { Scope = "Global", TenantSelectionPending = true },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(AuthenticationMappingConfig.ToTenantSelectionResponse(
                tokens, sessions.BuildUserResponse(user, access), selectable));
        }

        // ---- 10. Issue the session --------------------------------------------------------------------
        return await CompleteSignInAsync(
            user, tenant, businessUnit, request, client, identifier, now,
            usedTrustedDevice: knownDevice is not null, cancellationToken);
    }

    /// <summary>
    /// Everything after the last gate: mint the session, stamp the capture columns, record the
    /// attempt, write the audit row. Shared with the MFA verification handler, which arrives
    /// here having satisfied the second factor.
    /// </summary>
    internal async Task<Result<SignInResponse>> CompleteSignInAsync(
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        SignInRequest request,
        ClientInfo client,
        string identifier,
        DateTimeOffset now,
        bool usedTrustedDevice,
        CancellationToken cancellationToken)
    {
        var scope = user.IsSuperAdmin ? AccessScopeType.Global : AccessScopeType.Tenant;
        var access = await effectiveAccess.ResolveAsync(user, tenant?.Id, cancellationToken);

        var tokens = await sessions.IssueAsync(
            user, tenant, businessUnit, scope, request.ClientType,
            mfaCompleted: true, request.RememberMe, tenantContext.HostName,
            request.DeviceIdentifier, request.DeviceName ?? client.DeviceName,
            usedTrustedDevice, cancellationToken);

        ApplyLoginCapture(user, client, request, now);

        await RecordAttemptAsync(
            user, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.Succeeded,
            client, request, now, 0, 0, false, cancellationToken, tokens.SessionId);

        await audit.WriteAsync(
            AuditActionCodes.SignInSucceeded, nameof(User), user.Id, user.DisplayName,
            new { TenantId = tenant?.Id, Scope = scope.ToString(), request.ClientType },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // An administrator-set temporary password gets the person in, but nowhere else until
        // they change it. The client routes straight to the change-password screen.
        if (user.MustChangePassword)
        {
            return Result.Success(AuthenticationMappingConfig.ToPasswordChangeRequiredResponse(
                tokens, sessions.BuildUserResponse(user, access),
                sessions.BuildTenantResponse(tenant, businessUnit, scope, isTenantMode: false)));
        }

        return Result.Success(AuthenticationMappingConfig.ToSuccessResponse(
            tokens,
            sessions.BuildUserResponse(user, access),
            sessions.BuildTenantResponse(tenant, businessUnit, scope,
                isTenantMode: scope == AccessScopeType.Global && tenant is not null)));
    }

    /// <summary>
    /// A wrong password: increment the counter, lock the account when it trips, and tell the
    /// person how many tries remain once it gets low.
    ///
    /// The brief asks for five failures and a fifteen-minute lockout. Both come from the
    /// Organisation policy rather than a constant, so an Organisation can tighten them, with
    /// the platform values as the floor.
    /// </summary>
    private async Task<Result<SignInResponse>> HandleFailedPasswordAsync(
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        string identifier,
        ClientInfo client,
        SignInRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var maximumAttempts = tenant?.MaximumFailedAccessAttempts ?? _security.MaximumFailedAccessAttempts;
        var lockoutMinutes = tenant?.LockoutDurationMinutes ?? _security.LockoutMinutes;

        user.AccessFailedCount += 1;
        user.LastFailedLoginAtUtc = now;
        user.LastFailedLoginIpAddress = currentUser.IpAddress;

        var triggeredLockout = false;
        var attemptsRemaining = Math.Max(0, maximumAttempts - user.AccessFailedCount);

        if (user.LockoutEnabled && user.AccessFailedCount >= maximumAttempts)
        {
            user.LockoutEnd = now.AddMinutes(lockoutMinutes);
            triggeredLockout = true;
            attemptsRemaining = 0;
        }

        await RecordAttemptAsync(
            user, identifier, businessUnit.Id, tenant?.Id, SignInOutcome.InvalidCredentials,
            client, request, now, user.AccessFailedCount, attemptsRemaining, false,
            cancellationToken, triggeredLockout: triggeredLockout, lockoutEndUtc: user.LockoutEnd);

        if (triggeredLockout)
        {
            await audit.WriteAsync(
                AuditActionCodes.SignInLockedOut, nameof(User), user.Id, AuditResult.Denied,
                user.DisplayName,
                new { user.AccessFailedCount, LockoutEndUtc = user.LockoutEnd },
                cancellationToken: cancellationToken);

            // Told, because a lockout somebody did not cause is exactly what they need to know.
            await notifications.SendAccountLockedAsync(
                user, tenant, businessUnit, user.LockoutEnd!.Value, currentUser.IpAddress, cancellationToken);
        }
        else
        {
            await audit.WriteAsync(
                AuditActionCodes.SignInFailed, nameof(User), user.Id, AuditResult.Denied,
                user.DisplayName, new { AttemptsRemaining = attemptsRemaining },
                cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (triggeredLockout)
        {
            return Result.Failure<SignInResponse>(Error.AccountLocked(lockoutMinutes));
        }

        // The remaining count appears only once it is low. Showing it from the first failure
        // would hand somebody probing the account a progress bar.
        var error = attemptsRemaining <= _security.WarnWhenAttemptsRemaining
            ? Error.InvalidCredentials(
                $"The sign-in details are incorrect. {attemptsRemaining} attempt(s) remaining before this account is locked.")
            : Error.InvalidCredentials();

        return Result.Failure<SignInResponse>(error);
    }

    /// <summary>
    /// Account-level reasons to refuse, each with its own message because each has a different
    /// remedy. Returns null when the account is fine.
    /// </summary>
    private static (SignInOutcome Outcome, Error Error)? EvaluateAccountState(User user, DateTimeOffset now) =>
        user.Status switch
        {
            UserStatus.Suspended => (SignInOutcome.Suspended, Error.AccountSuspended()),
            UserStatus.Deactivated => (SignInOutcome.Deactivated, Error.AccountDeactivated()),
            UserStatus.Withdrawn => (SignInOutcome.Deactivated, Error.AccountDeactivated()),
            UserStatus.Expired => (SignInOutcome.Expired, Error.AccessWindowClosed()),
            UserStatus.Invited => (SignInOutcome.NotActivated, Error.AccountNotActivated()),
            UserStatus.Draft => (SignInOutcome.NotActivated, Error.AccountNotActivated()),
            UserStatus.Active when user.IsOutsideAccessWindow(now) =>
                (SignInOutcome.Expired, Error.AccessWindowClosed()),
            _ => null
        };

    /// <summary>
    /// Organisation-level reasons to refuse.
    /// </summary>
    /// <remarks>
    /// FOR NEWCOMERS: this answers "your password was right, but may you come in GIVEN THE STATE
    /// OF YOUR ORGANISATION?" It is a separate question from "is your password right" and from
    /// "is your own account active", and each is checked on its own so the message a person gets
    /// tells them the truth about what is actually wrong.
    ///
    /// The shape of the rule lives on <see cref="Tenant.PermitsSession"/>. This method exists to
    /// turn a "no" into the RIGHT no - a different message for suspended, for rejected, and for
    /// still-being-reviewed - because "you cannot sign in" with no reason is how a support ticket
    /// gets raised.
    ///
    /// TWO THINGS HERE ARE EASY TO GET WRONG, so they are spelled out:
    ///
    /// 1. THE ADMINISTRATOR MUST BE LET IN WHILE THE ORGANISATION IS STILL ONBOARDING. Completing
    ///    the profile, attaching the registration documents and submitting for approval are jobs
    ///    only they can do. Refusing them created a deadlock with no way out: the profile could
    ///    never be finished, so it could never be submitted, so SuperAdmin could never approve it,
    ///    so the Organisation could never go live - and the one account that could have broken the
    ///    cycle was the one being turned away. Rejected counts as onboarding for the same reason:
    ///    being told what is wrong and then locked out of fixing it is the same dead end wearing a
    ///    politer message.
    ///
    /// 2. APPROVED IS NOT THE SAME AS ACTIVE. Approved means "we have accepted this organisation";
    ///    Active means "it is switched on". They are usually the same moment, because the reviewer
    ///    ticks "activate immediately" - but they are kept separate so an Organisation can be
    ///    approved today and go live on an agreed date. At Approved the ADMINISTRATOR may sign in
    ///    and get on with setting things up; their staff may not, because for everybody else there
    ///    is still nothing there. Before this was handled, Approved-without-activation locked out
    ///    every single account including the administrator's, which made a state the domain
    ///    deliberately supports impossible to actually sit in.
    /// </remarks>
    private static Error? EvaluateTenantState(Tenant tenant, User user) => tenant.Status switch
    {
        TenantStatus.Active => null,

        // Suspended and Archived are decisions ABOUT the Organisation rather than steps on the
        // way in, and they apply to the administrator as much as to anybody else.
        TenantStatus.Suspended => Error.TenantSuspended(),
        TenantStatus.Archived => Error.TenantInactive("This organisation is no longer active."),

        // Onboarding, and approved-but-not-yet-switched-on, are both the administrator's to work
        // in. One predicate decides it, shared with the token-refresh path so the two cannot
        // disagree and sign somebody out minutes after letting them in.
        _ when tenant.PermitsSession(user.IsTenantAdmin) => null,

        TenantStatus.Rejected => Error.TenantNotApproved(
            "This organisation has not been approved. Your administrator has been told what is needed."),

        TenantStatus.Approved => Error.TenantInactive(
            "This organisation has been approved but is not switched on yet. "
            + "Your administrator will let you know when it is ready."),

        _ when tenant.IsOnboarding => Error.TenantNotApproved(),
        _ => Error.TenantInactive()
    };

    /// <summary>
    /// The remembered-device row this browser presented, if it still counts as one.
    ///
    /// Returns the ROW rather than a boolean because two callers need different things from it:
    /// the MFA gate wants to know when the trust was established, and "remember this device"
    /// wants to extend the existing row instead of adding a second one for the same browser.
    /// </summary>
    private async Task<TrustedDevice?> ResolveTrustedDeviceAsync(
        User user, string? trustedDeviceToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        TrustedDevice? device = null;

        if (!string.IsNullOrWhiteSpace(trustedDeviceToken))
        {
            device = await security.FindTrustedDeviceAsync(
                user.Id, tokenHasher.Hash(trustedDeviceToken), cancellationToken);
        }

        if (device is null || !device.IsTrusted(now))
        {
            return null;
        }

        device.LastSeenAtUtc = now;
        device.IpAddress = currentUser.IpAddress ?? device.IpAddress;

        return device;
    }

    /// <summary>
    /// Remembers this browser, or renews the row that already stands for it.
    ///
    /// ONLY THE HASH OF THE TOKEN IS STORED; the plaintext goes out through
    /// <see cref="TrustedDeviceTokenAccessor"/> so the API layer can put it in an HttpOnly
    /// cookie, and it is never written to the database or the response body.
    ///
    /// THE EXISTING ROW IS RENEWED RATHER THAN DUPLICATED. Somebody who signs in every morning
    /// with the box ticked would otherwise collect one "Remembered device" per morning, all of
    /// them the same laptop, on the very list whose job is to make an unfamiliar device stand
    /// out. A browser that presented no cookie is matched on its device identifier for the same
    /// reason.
    /// </summary>
    private async Task RememberDeviceAsync(
        User user,
        TrustedDevice? existing,
        SignInRequest request,
        ClientInfo client,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiresAt = now.AddDays(_security.TrustedDeviceDays);

        // No cookie, but this browser may already have a row from a session whose cookie has
        // since been cleared. Matching on the identifier the client mints and keeps is what
        // stops the same laptop appearing three times.
        existing ??= await security.FindTrustedDeviceByIdentifierAsync(
            user.Id, request.DeviceIdentifier, now, cancellationToken);

        if (existing is not null)
        {
            existing.DeviceName = request.DeviceName ?? existing.DeviceName ?? client.DeviceName;
            existing.DeviceIdentifier = request.DeviceIdentifier ?? existing.DeviceIdentifier;
            existing.UserAgent = currentUser.UserAgent ?? existing.UserAgent;
            existing.Browser = client.Browser ?? existing.Browser;
            existing.OperatingSystem = client.OperatingSystem ?? existing.OperatingSystem;
            existing.ExpiresAtUtc = expiresAt;
            existing.LastSeenAtUtc = now;

            // The browser still holds the plaintext when it presented one, so handing the SAME
            // value back renews the cookie's lifetime rather than letting it lapse before the row
            // it points at. When it presented none — the identifier match above — the row is
            // re-keyed to a fresh token, because there is no plaintext left to hand back.
            if (!string.IsNullOrWhiteSpace(request.TrustedDeviceToken))
            {
                TrustedDeviceTokenAccessor.Set(request.TrustedDeviceToken);
                return;
            }

            var replacement = tokenHasher.GenerateToken();
            existing.DeviceTokenHash = tokenHasher.Hash(replacement);
            TrustedDeviceTokenAccessor.Set(replacement);
            return;
        }

        var token = tokenHasher.GenerateToken();

        var device = new TrustedDevice
        {
            TenantId = user.TenantId ?? Guid.Empty,
            BusinessUnitId = user.BusinessUnitId,
            UserId = user.Id,
            DeviceTokenHash = tokenHasher.Hash(token),
            DeviceName = request.DeviceName ?? client.DeviceName,
            DeviceIdentifier = request.DeviceIdentifier,
            ClientType = request.ClientType,
            UserAgent = currentUser.UserAgent,
            Browser = client.Browser,
            OperatingSystem = client.OperatingSystem,
            IpAddress = currentUser.IpAddress,
            TrustedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            LastSeenAtUtc = now
        };

        await security.AddTrustedDeviceAsync(device, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.DeviceTrusted, nameof(TrustedDevice), null,
            device.DeviceName, new { device.Browser, device.OperatingSystem },
            cancellationToken: cancellationToken);

        TrustedDeviceTokenAccessor.Set(token);
    }

    /// <summary>
    /// The last-session capture the brief asks for: when, from where, on what, and with which
    /// device identifier.
    /// </summary>
    private void ApplyLoginCapture(User user, ClientInfo client, SignInRequest request, DateTimeOffset now)
    {
        user.LastLoginAtUtc = now;
        user.LastSignedInAtUtc = now;
        user.LastLoginIpAddress = currentUser.IpAddress;
        user.LastLoginUserAgent = currentUser.UserAgent;
        user.LastLoginClientType = request.ClientType;
        user.LastLoginBrowser = client.Browser;
        user.LastLoginOperatingSystem = client.OperatingSystem;
        user.LastLoginDeviceIdentifier = request.DeviceIdentifier;
    }

    /// <summary>
    /// Appends the attempt row. Called on EVERY path, including the ones where no account was
    /// found — an attempt against an address that does not exist is precisely the pattern
    /// worth being able to see.
    /// </summary>
    private async Task RecordAttemptAsync(
        User? user,
        string identifier,
        Guid businessUnitId,
        Guid? tenantId,
        SignInOutcome outcome,
        ClientInfo client,
        SignInRequest request,
        DateTimeOffset now,
        int failedCount,
        int attemptsRemaining,
        bool mfaChallenged,
        CancellationToken cancellationToken,
        Guid? sessionId = null,
        bool triggeredLockout = false,
        DateTimeOffset? lockoutEndUtc = null)
    {
        await security.AddSignInAttemptAsync(new SignInAttempt
        {
            BusinessUnitId = businessUnitId,
            TenantId = tenantId,
            UserId = user?.Id,
            AttemptedIdentifier = identifier.Length > 320 ? identifier[..320] : identifier,
            HostName = tenantContext.HostName,
            Outcome = outcome,
            Succeeded = outcome == SignInOutcome.Succeeded,
            AttemptedAtUtc = now,
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            ClientType = request.ClientType,
            Browser = client.Browser,
            OperatingSystem = client.OperatingSystem,
            DeviceIdentifier = request.DeviceIdentifier,
            FailedAttemptCount = failedCount,
            AttemptsRemaining = attemptsRemaining,
            TriggeredLockout = triggeredLockout,
            LockoutEndUtc = lockoutEndUtc,
            MfaChallenged = mfaChallenged,
            SessionId = sessionId,
            CorrelationId = currentUser.CorrelationId
        }, cancellationToken);
    }
}
