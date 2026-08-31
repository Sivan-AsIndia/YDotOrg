using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A single-use token for password reset, e-mail confirmation and account unlock.
///
/// TENANT-OWNED, WHICH IS THE POINT. The brief notes that because normal users are
/// Tenant-specific, password reset and e-mail confirmation have to be too. A reset link
/// issued for john@gmail.com in TEN001 resolves to the row that names TEN001 and that user
/// id, so it can never reset the unrelated john@gmail.com in TEN002.
///
/// Hashed, expiring and single-use, exactly like an invitation.
/// </summary>
public class RecoveryToken : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public RecoveryTokenPurpose Purpose { get; set; }

    /// <summary>SHA-256 of the secret. The plaintext exists only in the e-mail.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// For an e-mail-confirmation or identifier-change token, the address being proved.
    /// Null for a password reset, which proves nothing new.
    /// </summary>
    public string? TargetValue { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public string? ConsumedFromIpAddress { get; set; }

    public DateTimeOffset? InvalidatedAtUtc { get; set; }

    /// <summary>
    /// Issuing a new token of the same purpose invalidates the previous one, so a mailbox
    /// full of old reset links has exactly one that works.
    /// </summary>
    public string? InvalidationReason { get; set; }

    public string? RequestedFromIpAddress { get; set; }

    public string? RequestedUserAgent { get; set; }

    public bool IsRedeemable(DateTimeOffset asOf) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && ExpiresAtUtc > asOf;
}
