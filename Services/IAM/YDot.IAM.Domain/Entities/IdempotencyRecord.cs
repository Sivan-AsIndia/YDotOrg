using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// Remembers the answer given to a request that carried an Idempotency-Key header.
///
/// A phone on a flaky connection retries. Without this, "invite these forty users" sent
/// twice creates eighty invitations. With it, the second call finds the stored response and
/// replays it, so the retry is genuinely free.
///
/// The key is scoped by Tenant as well as by user, so two Organisations cannot collide on a
/// client-chosen string.
/// </summary>
public class IdempotencyRecord : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    public Guid? TenantId { get; set; }

    /// <summary>The value of the Idempotency-Key header, as sent.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Which endpoint it was for, so the same key on a different route is not a match.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    /// <summary>
    /// Hash of the request body. A repeat of the same key with a DIFFERENT body is a client
    /// bug rather than a retry, and is refused rather than silently answered with the old
    /// result.
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;

    public int ResponseStatusCode { get; set; }

    /// <summary>The serialised response to replay.</summary>
    public string? ResponseBody { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool IsUsable(DateTimeOffset asOf) => ExpiresAtUtc > asOf;
}
