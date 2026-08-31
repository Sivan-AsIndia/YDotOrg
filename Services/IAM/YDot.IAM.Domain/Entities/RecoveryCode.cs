using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One single-use backup code, for getting back in when the second factor is unavailable.
///
/// Codes are generated as a batch, shown to the person exactly once, and stored only as
/// hashes. Redeeming one marks that row spent rather than deleting it, so the security
/// screen can honestly report how many remain and when the last one was used.
/// </summary>
public class RecoveryCode : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 of the code. The plaintext is shown once at generation and never stored.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Groups the batch, so generating a new set can retire the previous one wholesale.</summary>
    public Guid BatchId { get; set; }

    public DateTimeOffset GeneratedAtUtc { get; set; }

    public DateTimeOffset? RedeemedAtUtc { get; set; }

    public string? RedeemedFromIpAddress { get; set; }

    /// <summary>Set when a newer batch supersedes this one.</summary>
    public DateTimeOffset? RetiredAtUtc { get; set; }

    public bool IsRedeemable => RedeemedAtUtc is null && RetiredAtUtc is null;
}
