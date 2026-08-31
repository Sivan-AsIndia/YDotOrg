using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A device the person asked the platform to remember, so it stops asking for a second
/// factor on every sign-in from their own laptop.
///
/// The trust is a convenience, and it is deliberately weak on its own: the cookie proves
/// only that this browser has been here before. It suppresses the MFA prompt for ordinary
/// sign-in and nothing else — a privileged action still triggers a step-up regardless.
/// Trust also expires, so a machine that was trusted a year ago is challenged again.
/// </summary>
public class TrustedDevice : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 of the device token held in the browser cookie.</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;

    /// <summary>What the person calls it: "My laptop".</summary>
    public string? DeviceName { get; set; }

    /// <summary>Stable per-device identifier supplied by the client, where there is one.</summary>
    public string? DeviceIdentifier { get; set; }

    public ClientType ClientType { get; set; } = ClientType.Web;

    public string? UserAgent { get; set; }

    public string? Browser { get; set; }

    public string? OperatingSystem { get; set; }

    public string? IpAddress { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset TrustedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevocationReason { get; set; }

    public bool IsTrusted(DateTimeOffset asOf) => RevokedAtUtc is null && ExpiresAtUtc > asOf;
}
