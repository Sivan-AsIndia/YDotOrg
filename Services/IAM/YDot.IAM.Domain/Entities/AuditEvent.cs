using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An immutable, append-only record of something that happened, section 3.8.
///
/// TENANT IS NULLABLE HERE, UNLIKE ALMOST EVERYWHERE ELSE. Two kinds of event have no
/// Organisation: a platform action by SuperAdmin (creating an Organisation, approving one)
/// and a failed sign-in whose host never resolved. Forcing a TenantId on those would mean
/// inventing one, and an invented Organisation id in an audit trail is worse than an honest
/// null.
///
/// SUPERADMIN ACTIONS RECORD BOTH. When a root user acts inside a selected Organisation,
/// <see cref="TenantId"/> is that Organisation and <see cref="ActorScope"/> is Global. That
/// pairing is what lets the Organisation see "somebody from the platform did this to my
/// data" without the row pretending the actor was one of their own users.
///
/// NOTHING SENSITIVE GOES IN HERE. <see cref="Metadata"/> is redacted before it is written:
/// no passwords, no tokens, no full contact details. An audit trail that leaks the thing it
/// was auditing is a liability rather than a control.
/// </summary>
public class AuditEvent : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    /// <summary>Null for a platform-level action or an unresolved host.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Null for a trusted system actor such as a background job.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Denormalised so the trail still reads if the actor is later removed.</summary>
    public string? ActorDisplayName { get; set; }

    /// <summary>Global when a root user did this; Tenant otherwise.</summary>
    public AccessScopeType ActorScope { get; set; } = AccessScopeType.Tenant;

    /// <summary>Stable dotted code, for example <c>iam.user.suspend</c>. Max 100.</summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>The registered auditable type: User, Role, Tenant.</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Null only for a collection-level or system event.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Readable name of the target at the time, so the row survives a rename.</summary>
    public string? TargetDisplayName { get; set; }

    public AuditResult Result { get; set; } = AuditResult.Succeeded;

    /// <summary>Required for a privileged override. Max 1000.</summary>
    public string? Reason { get; set; }

    /// <summary>Ties the request, the log line and this row together. Max 80.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public ClientType ClientType { get; set; } = ClientType.Unknown;

    public Guid? SessionId { get; set; }

    /// <summary>
    /// Redacted JSON detail: which fields changed, what the old and new values were for the
    /// non-sensitive ones. Never credentials, tokens or unmasked personal data.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// True when the action exercised a permission marked sensitive, which is what drives
    /// the longer retention and the tighter read permission on this row.
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>The API route that produced it, for tracing back to code.</summary>
    public string? RequestPath { get; set; }
}
