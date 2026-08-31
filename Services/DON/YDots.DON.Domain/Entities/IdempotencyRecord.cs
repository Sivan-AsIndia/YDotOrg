using YDots.DON.Domain.Common;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// One already-processed Idempotency-Key (table don_idempotency_keys).
///
/// Section 10: a command triggered by a webhook, an import or a message must carry an
/// Idempotency-Key. If the caller retries after an uncertain response, the key is found here
/// and the original reference is returned instead of creating a second record.
/// </summary>
public class IdempotencyRecord : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    /// <summary>The value of the Idempotency-Key request header.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Route the key was used on, so the same key on two endpoints does not collide.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Identifier of the record the first call created.</summary>
    public Guid ResourceId { get; set; }

    /// <summary>Stable business reference of that record, for example DON-2026-000184.</summary>
    public string ResourceReference { get; set; } = string.Empty;
}
