using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A half-finished sensitive action, parked while the person proves it is really them.
///
/// The step-up flow is: fill in the form, get asked to re-authenticate, come back. Without
/// somewhere to put the form, coming back means typing it all again — so people learn to
/// avoid the sensitive screens, which is the opposite of what the control is for. The draft
/// is short-lived, single-use, and only ever readable by the person who created it.
/// </summary>
public class ProtectedActionDraft : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Which action was being taken, so the resume path knows where to send them.</summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>The record being acted on, when there is one.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Serialised form state. Never credentials.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Opaque handle the client holds while it goes off to re-authenticate.</summary>
    public string DraftToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    /// <summary>The session that created it. A different session may not resume it.</summary>
    public Guid? SessionId { get; set; }

    public bool IsUsable(DateTimeOffset asOf) => ConsumedAtUtc is null && ExpiresAtUtc > asOf;
}
