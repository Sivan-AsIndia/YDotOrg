using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One rung of the Organisation lifecycle ladder, appended every time
/// <see cref="Tenant.Status"/> moves.
///
/// <see cref="Tenant"/> keeps only the current status plus a handful of "when was it
/// approved" columns. That is enough to render a badge but not enough to answer the
/// questions that actually get asked during onboarding: how long did this Organisation sit
/// in review, how many times was it sent back, who rejected it the first time and what did
/// they say. This table answers those, and it is append-only so the answer cannot be
/// rewritten after the fact.
///
/// It is what the Organisation timeline on the owner and SuperAdmin screens is drawn from.
/// </summary>
public class TenantStatusHistory : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>Null for the very first row, where the Organisation was created.</summary>
    public TenantStatus? FromStatus { get; set; }

    public TenantStatus ToStatus { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Null when the platform moved the record rather than a person.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Denormalised so the timeline still reads correctly if the actor is later removed.</summary>
    public string? ActorDisplayName { get; set; }

    /// <summary>Required for a rejection or a suspension; optional elsewhere.</summary>
    public string? Reason { get; set; }

    public string? Notes { get; set; }

    /// <summary>Ties this move back to the request that caused it.</summary>
    public string? CorrelationId { get; set; }
}
