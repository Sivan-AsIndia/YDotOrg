using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One signed-in session, section 3.7. This is the row that "User security, devices and
/// sessions" lists and that "sign out everywhere" revokes.
///
/// WHAT A SESSION IS HERE. It is the server-side record that an access token belongs to.
/// The token itself is stateless and carries a <c>session_id</c> claim pointing at this
/// row; the row is what can be revoked. Without it, a JWT could not be cancelled before its
/// expiry, and "sign out on my lost phone" would be a fifteen-minute promise rather than an
/// immediate one.
///
/// TENANT CONTEXT IS RECORDED, NOT INFERRED. <see cref="OperatingTenantId"/> is the
/// Organisation the session is currently working inside. For a Tenant user that is always
/// their own and never changes. For SuperAdmin it is whichever Organisation they selected,
/// and it moves when they switch — while their <c>User.TenantId</c> stays null throughout.
/// Recording it here is what lets the audit trail say which Organisation a root user was
/// standing in when they did something.
/// </summary>
public class UserSession : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// SHA-256 of the session token. Never the token itself, for the same reason passwords
    /// are hashed: a stolen database must not yield usable sessions.
    /// </summary>
    public string SessionTokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The Organisation this session is operating in. Equals TenantId for a normal user;
    /// for SuperAdmin it is the selected Organisation and may change during the session.
    /// </summary>
    public Guid? OperatingTenantId { get; set; }

    /// <summary>Global for a SuperAdmin session, Tenant for everybody else.</summary>
    public AccessScopeType AccessScope { get; set; } = AccessScopeType.Tenant;

    // ---- Device and client capture, required by the brief ----------------------------------

    /// <summary>Friendly description built from the user agent: "Chrome on Windows".</summary>
    public string? DeviceName { get; set; }

    /// <summary>Stable per-device identifier from the client, where one is supplied.</summary>
    public string? DeviceIdentifier { get; set; }

    public ClientType ClientType { get; set; } = ClientType.Web;

    public string? UserAgent { get; set; }

    /// <summary>Parsed for display: "Chrome", "Firefox", "Safari".</summary>
    public string? Browser { get; set; }

    public string? OperatingSystem { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Coarse location derived from the address, for the "was this you?" prompt.</summary>
    public string? Location { get; set; }

    // ---- Lifetime ----------------------------------------------------------------------------

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>Moved forward on each authenticated request; drives the idle timeout.</summary>
    public DateTimeOffset LastActivityAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    /// <summary>Required when revoked, so the security screen can explain what happened.</summary>
    public string? RevocationReason { get; set; }

    /// <summary>True once the second factor has been satisfied for this session.</summary>
    public bool MfaCompleted { get; set; }

    public DateTimeOffset? MfaCompletedAtUtc { get; set; }

    /// <summary>
    /// When the person last proved it was really them for a sensitive action. A step-up is
    /// required again once this is older than the configured window.
    /// </summary>
    public DateTimeOffset? LastReauthenticatedAtUtc { get; set; }

    /// <summary>True when the session was opened on a device the user has marked trusted.</summary>
    public bool IsTrustedDevice { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>True when the session is still usable at the given moment.</summary>
    public bool IsActive(DateTimeOffset asOf) => RevokedAtUtc is null && ExpiresAtUtc > asOf;

    /// <summary>True when the session has been idle longer than the Organisation allows.</summary>
    public bool IsIdleExpired(DateTimeOffset asOf, int idleTimeoutMinutes) =>
        LastActivityAtUtc.AddMinutes(idleTimeoutMinutes) <= asOf;
}
