using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An integration event waiting to be published.
///
/// THE POINT IS ATOMICITY. "Create the user AND tell the other services" cannot be two
/// separate operations, because the process can die between them and leave the two halves
/// disagreeing forever. Writing the message into this table inside the same transaction as
/// the user means either both happened or neither did; a publisher then drains the table
/// afterwards and retries on its own schedule.
/// </summary>
public class OutboxMessage : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    /// <summary>Null for a platform-level event with no owning Organisation.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The event type name, for example <c>iam.user.activated</c>.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Serialised event body. Redacted: no credentials, no tokens.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set once the message has failed too many times to keep trying.</summary>
    public bool IsDeadLettered { get; set; }

    public string? CorrelationId { get; set; }

    public bool IsPending => ProcessedAtUtc is null && !IsDeadLettered;
}
