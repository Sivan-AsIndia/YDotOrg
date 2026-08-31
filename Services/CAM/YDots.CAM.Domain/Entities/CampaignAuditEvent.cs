using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One row of the append-only campaign audit trail.
///
/// IT IS NOT AN <see cref="AuditEntity"/>, AND IT MUST NOT BE. An audit row is written once and
/// never updated, so UpdatedAt, UpdatedBy and a concurrency version would be columns that stay
/// permanently null - and inheriting them would invite something to update a row that is
/// supposed to be immutable. It keeps its own <see cref="OccurredAtUtc"/> instead.
///
/// IT IS NOT <see cref="ITenantOwned"/> EITHER, which is a deliberate exception rather than an
/// oversight. A query filter here would make an audit row invisible whenever a request has no
/// resolved Organisation - including the failed and denied attempts that are exactly what an
/// investigation goes looking for. <see cref="TenantId"/> is recorded and filtered on
/// explicitly by the audit read endpoints instead.
/// </summary>
public sealed class CampaignAuditEvent : BaseEntity
{
    /// <summary>Null where the attempt failed before an Organisation could be resolved.</summary>
    public Guid? TenantId { get; set; }

    public Guid? BusinessUnitId { get; set; }

    /// <summary>Null for an anonymous or pre-authentication attempt.</summary>
    public Guid? ActorUserId { get; set; }

    public string ActionCode { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public AuditResult Result { get; set; }

    public string? Reason { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>
    /// When it happened. Its own column rather than an inherited CreatedAtUtc, because this is
    /// the only timestamp the row has and calling it "created" would be misleading.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; set; }
}
