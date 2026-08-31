using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An invitation to join one Organisation, section 9 of the brief.
///
/// THE INVITATION IS TENANT-SPECIFIC, AND THAT IS THE WHOLE POINT. The brief warns that an
/// invitation "must never accidentally activate/create a user in another Tenant", and that
/// if the same address already exists elsewhere "that does NOT mean the user record from
/// that Tenant should be reused". Both are structural here:
///
///   • The row carries TenantId and BusinessUnitId, so acceptance can only ever act inside
///     one Organisation.
///   • Acceptance creates or activates the user whose id is on THIS row. It never looks the
///     e-mail up globally, because a global lookup is exactly how john@gmail.com in TEN002
///     would end up activated by an invitation meant for TEN001.
///
/// THE TOKEN IS STORED AS A HASH. <see cref="TokenHash"/> holds a SHA-256 of the secret,
/// never the secret itself. The plaintext exists only in the e-mail. A leaked database
/// therefore yields nothing usable, exactly as with a password.
///
/// SINGLE USE. Acceptance sets the status and stamps <see cref="AcceptedAtUtc"/>, and a
/// token that has already been spent is refused even if it has not expired.
/// </summary>
public class UserInvitation : TenantEntity
{
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// The user record this invitation belongs to. Created in Draft/Invited state at the
    /// moment the invitation is issued, so the invitation always points at exactly one
    /// person inside exactly one Organisation and acceptance has nothing to resolve.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>As typed, for the e-mail.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Lower-cased, and the half that is indexed.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    public InvitationType InvitationType { get; set; } = InvitationType.TenantUser;

    /// <summary>The role the person is given on acceptance. Null falls back to the Tenant default role.</summary>
    public Guid? InitialRoleId { get; set; }

    public Role? InitialRole { get; set; }

    /// <summary>
    /// SHA-256 of the invitation secret. The plaintext is generated once, put in the link,
    /// and never persisted.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Short, non-secret, human-readable handle for the invitation, so support can talk
    /// about it on the phone without either party reading out the token.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset InvitedAtUtc { get; set; }

    public DateTimeOffset? AcceptedAtUtc { get; set; }

    /// <summary>Where the acceptance came from, kept for the audit trail.</summary>
    public string? AcceptedFromIpAddress { get; set; }

    public string? AcceptedUserAgent { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>How many times the invitation has been re-sent, so a resend loop is visible.</summary>
    public int ResendCount { get; set; }

    public DateTimeOffset? LastSentAtUtc { get; set; }

    /// <summary>
    /// The host the acceptance link points at, for example ten1.ngoplanet.com. Captured when
    /// the invitation is issued rather than rebuilt at send time, so a later change to the
    /// Organisation primary domain cannot silently redirect an outstanding invitation.
    /// </summary>
    public string? InvitationHostName { get; set; }

    /// <summary>Free-text note from the inviter, included in the e-mail.</summary>
    public string? Message { get; set; }

    /// <summary>True when the token may still be redeemed at the given moment.</summary>
    public bool IsRedeemable(DateTimeOffset asOf) =>
        Status is InvitationStatus.Pending or InvitationStatus.Resent && ExpiresAtUtc > asOf;

    public bool IsExpired(DateTimeOffset asOf) => ExpiresAtUtc <= asOf;
}
