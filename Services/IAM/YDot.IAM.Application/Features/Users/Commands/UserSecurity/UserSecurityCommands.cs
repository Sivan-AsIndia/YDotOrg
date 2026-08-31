using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.Commands.UserSecurity;

// =============================================================================================
// IAM-USR-05: what an administrator can do to somebody else's security, one item at a time.
//
// WHY THESE ARE SEPARATE FROM force-sign-out. Signing somebody out of everything is a blunt
// instrument: it is right when an account is compromised and wrong when a person has simply
// left a laptop at an airport. Being able to end ONE session, or forget ONE device, is what
// lets the response match what actually happened.
//
// EVERY ONE OF THESE IS SCOPED TO THE NAMED USER. The session, device or factor is loaded and
// then checked to belong to the user in the route before anything is changed — an id belonging
// to somebody else is a 404, not a silent success. Without that check these endpoints would be
// a way to revoke any session in the Organisation by guessing an id.
// =============================================================================================

/// <summary>Ends one of a person's sessions, leaving their others alone.</summary>
public sealed record RevokeUserSessionCommand(Guid UserId, Guid SessionId, string? Reason);

/// <summary>Forgets one remembered device, so it must pass MFA again.</summary>
public sealed record RevokeUserTrustedDeviceCommand(Guid UserId, Guid DeviceId, string? Reason);

/// <summary>
/// Clears every enrolled factor so the person enrols again.
///
/// The case this exists for is a lost phone with the authenticator on it: the person cannot
/// complete MFA and cannot remove the factor themselves, because removing it needs a code
/// from it. Somebody with the permission has to break that loop.
/// </summary>
public sealed record ResetUserMfaCommand(Guid UserId, string Reason);

/// <summary>The security position of one account, as a file somebody can attach to a ticket.</summary>
public sealed record ExportUserSecurityQuery(Guid UserId);

/// <summary>One row of the security evidence file.</summary>
public sealed record UserSecurityEvidenceRow(
    string Section,
    string Item,
    string Detail,
    string Status,
    string RecordedAtUtc);

/// <summary>
/// Administrative security actions against a named account.
///
/// SEPARATE FROM <c>MySecurityFeatureHandler</c> ON PURPOSE. That one takes no user id at all
/// and always acts on the caller; this one always takes an id and is permission-gated. Merging
/// them into one handler with a "self" flag would put the two behind a single code path, and
/// the flag would become the only thing standing between a person and everybody else's
/// sessions.
/// </summary>
public sealed class UserSecurityCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    ISecurityRepository security,
    ISessionTokenService sessions,
    IUserReadService readService,
    IExportService exports,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    // =========================================================================================
    // Sessions
    // =========================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeUserSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        var session = await security.GetSessionAsync(command.SessionId, cancellationToken);

        // THE OWNERSHIP CHECK IS THE POINT. Without it, any session id in the system could be
        // ended through this route by putting a user id the caller can see in front of it.
        if (session is null || session.UserId != user.Id)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That session was not found."));
        }

        if (session.RevokedAtUtc is not null)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That session has already ended."));
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? "Ended by an administrator."
            : command.Reason;

        await sessions.RevokeAsync(session.Id, reason, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.SessionRevoked, nameof(UserSession), session.Id, user.DisplayName,
            new { session.DeviceName, session.IpAddress, TargetUserId = user.Id },
            reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            session.Id, "Revoked", session.Version,
            $"The session on {session.DeviceName ?? "that device"} has ended.", ["View"]));
    }

    // =========================================================================================
    // Trusted devices
    // =========================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        RevokeUserTrustedDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        var device = await security.GetTrustedDeviceAsync(command.DeviceId, cancellationToken);

        if (device is null || device.UserId != user.Id)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That device was not found."));
        }

        if (device.RevokedAtUtc is not null)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That device has already been forgotten."));
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? "Removed by an administrator."
            : command.Reason;

        device.RevokedAtUtc = clock.UtcNow;
        device.RevokedByUserId = currentUser.UserId;
        device.RevocationReason = reason;

        await audit.WriteAsync(
            AuditActionCodes.DeviceRevoked, nameof(TrustedDevice), device.Id, user.DisplayName,
            new { device.DeviceName, device.IpAddress, TargetUserId = user.Id },
            reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            device.Id, "Revoked", device.Version,
            $"{device.DeviceName ?? "That device"} will be asked to verify next time.", ["View"]));
    }

    // =========================================================================================
    // Second factors
    // =========================================================================================

    /// <summary>
    /// Clears every factor on an account.
    ///
    /// THE ACCOUNT IS LEFT REQUIRING MFA IF THE ORGANISATION REQUIRES IT. Resetting is not a
    /// way to switch the policy off — the person enrols again at their next sign-in. Turning
    /// the requirement off is a separate, deliberate edit to the account.
    ///
    /// Sessions go too. A session that has already passed MFA would otherwise keep working
    /// with a factor that no longer exists, which is exactly the access a reset is meant to
    /// interrupt when a device has been lost.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ResetUserMfaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        var methods = await security.GetMfaMethodsAsync(user.Id, cancellationToken);
        var usable = methods.Where(method => method.IsUsable).ToList();

        if (usable.Count == 0 && !user.MfaEnabled)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "There is nothing to reset: this account has no verification methods."));
        }

        foreach (var method in usable)
        {
            method.Status = MfaMethodStatus.Revoked;
            method.RevokedAtUtc = now;
            method.RevocationReason = command.Reason;
            method.IsPrimary = false;
        }

        user.AuthenticatorSecret = null;
        user.MfaEnabled = false;
        user.MfaEnrolledAtUtc = null;

        // The old backup codes go with the factors. Leaving them live would mean the reset
        // removed the factor and left the way round it in place.
        await security.RetireRecoveryCodesAsync(user.Id, now, cancellationToken);

        // And every remembered device, for the same reason: a device that is trusted skips the
        // challenge, so it would skip the re-enrolment this reset exists to force.
        await security.RevokeAllTrustedDevicesAsync(
            user.Id, command.Reason, now, cancellationToken);

        var revokedSessions = await sessions.RevokeAllAsync(
            user.Id, exceptSessionId: null, command.Reason, cancellationToken);

        user.SecurityStamp = Guid.NewGuid().ToString("N");

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        var stillRequired = user.IsMfaRequired(
            tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional);

        await audit.WriteAsync(
            AuditActionCodes.MfaReset, nameof(User), user.Id, user.DisplayName,
            new
            {
                MethodsRemoved = usable.Count,
                SessionsRevoked = revokedSessions,
                StillRequired = stillRequired,
            },
            command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version,
            stillRequired
                ? $"Removed {usable.Count} verification method(s). They will be asked to set one up at their next sign-in."
                : $"Removed {usable.Count} verification method(s).",
            ["View"]));
    }

    // =========================================================================================
    // Evidence
    // =========================================================================================

    /// <summary>
    /// The security position of one account as a file.
    ///
    /// WHAT IT IS FOR: an auditor asks "who could sign in as this person, from where, and when
    /// did they last do it". Answering that by taking screenshots of a web page is how it was
    /// done before, and screenshots are neither complete nor traceable.
    ///
    /// NO SECRET LEAVES IN IT. Not a hash, not a recovery code, not a session token — the file
    /// says a factor of a given kind exists and when it was last used, which is what the
    /// question actually needs. The export itself is audited with a reference that travels
    /// back on a header, so the file can be traced to whoever produced it.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportUserSecurityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ExportFile>(Error.UserNotFound());
        }

        var snapshot = await readService.GetSecurityAsync(user.Id, cancellationToken);

        if (snapshot is null)
        {
            return Result.Failure<ExportFile>(Error.UserNotFound());
        }

        var rows = new List<UserSecurityEvidenceRow>
        {
            new("Account", "Display name", snapshot.DisplayName ?? "", "", Stamp(clock.UtcNow)),
            new("Account", "Two-factor authentication",
                snapshot.MfaEnabled ? "Enrolled" : "Not enrolled",
                snapshot.IsMfaEffectivelyRequired ? "Required" : "Optional",
                Stamp(snapshot.MfaEnrolledAtUtc)),
            new("Account", "Recovery codes remaining",
                snapshot.RecoveryCodesRemaining.ToString(), "", ""),
            new("Account", "Password last changed", "", "",
                Stamp(snapshot.PasswordChangedAtUtc)),
            new("Account", "Must change password at next sign-in",
                snapshot.MustChangePassword ? "Yes" : "No", "", ""),
            new("Account", "Locked out",
                snapshot.IsLockedOut ? "Yes" : "No",
                snapshot.LockoutReason ?? "",
                Stamp(snapshot.LockoutEndUtc)),
            new("Account", "Failed sign-in attempts",
                snapshot.AccessFailedCount.ToString(),
                $"{snapshot.AttemptsRemaining} remaining before lockout", ""),
        };

        rows.AddRange((snapshot.MfaMethods ?? []).Select(method =>
            new UserSecurityEvidenceRow(
                "Verification method",
                method.MethodType.ToString(),
                method.MaskedDestination ?? method.Label ?? "",
                method.Status.ToString() + (method.IsPrimary ? " (primary)" : ""),
                Stamp(method.LastUsedAtUtc ?? method.VerifiedAtUtc))));

        rows.AddRange((snapshot.ActiveSessions ?? []).Select(session =>
            new UserSecurityEvidenceRow(
                "Active session",
                session.DeviceName ?? session.ClientType.ToString(),
                $"{session.Browser} on {session.OperatingSystem} from {session.IpAddress}",
                session.IsCurrent ? "Current" : "Active",
                Stamp(session.LastActivityAtUtc))));

        rows.AddRange((snapshot.TrustedDevices ?? []).Select(device =>
            new UserSecurityEvidenceRow(
                "Trusted device",
                device.DeviceName ?? device.ClientType.ToString(),
                $"{device.Browser} on {device.OperatingSystem} from {device.IpAddress}",
                device.IsExpired ? "Expired" : "Trusted",
                Stamp(device.LastSeenAtUtc ?? device.TrustedAtUtc))));

        rows.AddRange((snapshot.RecentAttempts ?? []).Select(attempt =>
            new UserSecurityEvidenceRow(
                "Sign-in attempt",
                attempt.OutcomeDisplay ?? attempt.Outcome.ToString(),
                $"{attempt.Browser} on {attempt.OperatingSystem} from {attempt.IpAddress}",
                attempt.TriggeredLockout ? "Triggered lockout"
                    : attempt.Succeeded ? "Succeeded" : "Failed",
                Stamp(attempt.AttemptedAtUtc))));

        var reference = tokenHasher.GenerateReference("EXP");
        var file = exports.ToCsv(
            rows, $"security-{user.Code ?? user.Id.ToString()}", reference);

        await audit.WriteAsync(
            AuditActionCodes.UserExported, nameof(User), user.Id, user.DisplayName,
            new { Kind = "SecurityEvidence", RowCount = rows.Count, Reference = reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }

    /// <summary>An instant in the one format the whole file uses, or blank when there isn't one.</summary>
    private static string Stamp(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
}
