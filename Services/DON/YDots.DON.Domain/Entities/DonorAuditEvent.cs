using YDots.DON.Domain.Common;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Entities;

/// <summary>
/// Append-only audit trail row (table don_audit_events). Section 10 of the contract: create,
/// edit, submit, approve, reject, cancel, archive, export and sensitive view all land here with
/// the actor, the target, the result, the reason and the correlation id.
/// </summary>
public class DonorAuditEvent : AuditEntity, IOrganisationOwned
{
    public Guid OrganisationId { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Stable dotted code, for example don.donor.approve.</summary>
    public string ActionCode { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    public AuditResult Result { get; set; } = AuditResult.Succeeded;

    /// <summary>Redacted: never a token, a payment instrument or a document byte.</summary>
    public string? Reason { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? IpAddress { get; set; }
}
