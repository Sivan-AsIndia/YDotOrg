using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A long-lived, single-use token that buys a new access token.
///
/// ROTATION AND REUSE DETECTION. Every refresh consumes the presented token and issues a
/// fresh one, chaining them through <see cref="ReplacedByTokenId"/>. If a token that has
/// already been consumed is presented again, that is not a mistake to forgive — it means
/// two parties hold the same token, so one of them stole it. The whole chain, and the
/// session behind it, is revoked. <see cref="IsReuseDetected"/> records that it happened.
///
/// This is why the Angular interceptor funnels every parallel 401 through a single shared
/// refresh: six simultaneous refreshes would present the same token six times and look
/// exactly like theft.
///
/// STORED AS A HASH. Same reasoning as sessions and invitations — the plaintext exists only
/// in the HttpOnly cookie held by the browser.
/// </summary>
public class RefreshToken : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The session this token refreshes. Revoking the session kills the token.</summary>
    public Guid SessionId { get; set; }

    public UserSession? Session { get; set; }

    /// <summary>SHA-256 of the token. The plaintext lives only in the client cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>Set the moment the token is exchanged. A second presentation after this is theft.</summary>
    public DateTimeOffset? ConsumedAtUtc { get; set; }

    /// <summary>The token issued in exchange for this one, forming the rotation chain.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>
    /// True when an already-consumed token was presented again. Kept as a flag rather than
    /// only a log line so the security screen can show the user that something happened.
    /// </summary>
    public bool IsReuseDetected { get; set; }

    public string? CreatedFromIpAddress { get; set; }

    public string? CreatedByUserAgent { get; set; }

    /// <summary>True when the token can still be exchanged: unconsumed, unrevoked, unexpired.</summary>
    public bool IsRedeemable(DateTimeOffset asOf) =>
        ConsumedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > asOf;
}
