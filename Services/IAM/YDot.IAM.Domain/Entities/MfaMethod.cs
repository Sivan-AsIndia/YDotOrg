using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One enrolled second factor, section 3.6.
///
/// THE DESTINATION IS NEVER STORED IN FULL. <see cref="MaskedDestination"/> holds
/// "***3210" or "as***@example.org", because the challenge screen has to be recognisable
/// to its owner without being readable to anybody who reaches the screen. The real
/// destination is read from the user record at send time, so it exists in exactly one place.
///
/// A shared secret, where the method needs one, lives in <see cref="SecretHash"/> and is
/// encrypted at rest by the DbContext value converter.
/// </summary>
public class MfaMethod : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public MfaMethodType MethodType { get; set; }

    /// <summary>What the person called it: "Work phone", "Yubikey". Helps when several are enrolled.</summary>
    public string? Label { get; set; }

    /// <summary>Masked form only. Never the full address or number.</summary>
    public string? MaskedDestination { get; set; }

    /// <summary>Encrypted at rest. Null for methods that need no shared secret.</summary>
    public string? SecretHash { get; set; }

    /// <summary>Only one active primary method per user; it is the one challenged by default.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Set only after a code issued against this method has actually been verified.</summary>
    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public MfaMethodStatus Status { get; set; } = MfaMethodStatus.Pending;

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>True when the method may be challenged: active and proven to work.</summary>
    public bool IsUsable => Status == MfaMethodStatus.Active && VerifiedAtUtc.HasValue;
}
