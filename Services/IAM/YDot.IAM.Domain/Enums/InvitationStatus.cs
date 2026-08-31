namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Section 9. An invitation is Tenant-specific and single-use.
///
/// <c>Revoked</c> and <c>Expired</c> are kept apart on purpose: revoked means a human took
/// it back, expired means the clock ran out, and the two want different messages on the
/// screen and different remedies.
/// </summary>
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3,
    Resent = 4
}
