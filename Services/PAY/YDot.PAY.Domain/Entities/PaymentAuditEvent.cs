using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// One row of the append-only payment audit trail.
///
/// IT IS NOT AN <see cref="AuditEntity"/>, AND IT MUST NOT BE. An audit row is written once and
/// never updated, so UpdatedAt, UpdatedBy and a concurrency version would stay permanently null
/// - and inheriting them would invite something to update a row that is supposed to be
/// immutable.
///
/// IT IS NOT <see cref="ITenantOwned"/> EITHER, which is a deliberate exception. A query filter
/// would make an audit row invisible whenever a request has no resolved Organisation - including
/// the anonymous public donation attempts and the failed webhooks that an investigation goes
/// looking for first. TenantId is recorded and filtered on explicitly by the audit endpoints.
/// </summary>
public sealed class PaymentAuditEvent : BaseEntity
{
    /// <summary>Null where the action happened before an Organisation could be resolved.</summary>
    public Guid? TenantId { get; set; }

    public Guid? BusinessUnitId { get; set; }

    /// <summary>Null for an anonymous donor action or a gateway callback.</summary>
    public Guid? ActorUserId { get; set; }

    public string ActionCode { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public AuditResult Result { get; set; }

    public string? Reason { get; set; }

    /// <summary>
    /// Scrubbed metadata about the action.
    ///
    /// NEVER A CARD NUMBER, a CVV or a gateway secret. An audit trail that leaks the thing it
    /// was auditing is a liability rather than a control.
    /// </summary>
    public string? Metadata { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
