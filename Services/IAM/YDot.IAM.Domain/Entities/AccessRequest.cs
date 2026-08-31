using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// Somebody asking for access they do not currently have, section 3.4.
///
/// This is the front door to privilege inside an Organisation, and it is a record rather
/// than a conversation so that every grant has a reason attached to it that an auditor can
/// read a year later.
///
/// MAKER AND CHECKER ARE DIFFERENT PEOPLE. The approver may not be the requester, which is
/// checked in the handler rather than only in the UI. Approval writes the actual
/// <see cref="UserRole"/> or <see cref="UserDataScope"/> row and stamps it with this request
/// id, so the grant can always be traced back to the justification that earned it.
/// </summary>
public class AccessRequest : TenantEntity
{
    /// <summary>System generated and unique inside the Tenant, for example AR-2026-00128.</summary>
    public string RequestNumber { get; set; } = string.Empty;

    /// <summary>The user the access is for. Often, but not always, the requester.</summary>
    public Guid RequestedForUserId { get; set; }

    public User? RequestedForUser { get; set; }

    public Guid RequestedByUserId { get; set; }

    public AccessRequestType RequestType { get; set; } = AccessRequestType.RoleAssignment;

    /// <summary>The role being asked for, when the request is for a role.</summary>
    public Guid? RoleId { get; set; }

    public Role? Role { get; set; }

    /// <summary>The permission code, when the request is for a single permission.</summary>
    public string? PermissionCode { get; set; }

    public DataScopeType? ScopeType { get; set; }

    public string? ScopeValue { get; set; }

    /// <summary>10 to 1000 characters. The whole point of the record.</summary>
    public string BusinessJustification { get; set; } = string.Empty;

    public DateTimeOffset AccessStartsAtUtc { get; set; }

    /// <summary>Required for temporary access; null means permanent.</summary>
    public DateTimeOffset? AccessEndsAtUtc { get; set; }

    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Draft;

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    /// <summary>Must differ from the requester.</summary>
    public Guid? DecidedByUserId { get; set; }

    public string? DecisionNotes { get; set; }

    public DateTimeOffset? WithdrawnAtUtc { get; set; }

    public string? WithdrawalReason { get; set; }

    /// <summary>Why it was sent back, and when. Shown to the requester so they know what to add.</summary>
    public string? ReturnReason { get; set; }

    public DateTimeOffset? ReturnedAtUtc { get; set; }

    public Guid? ReturnedByUserId { get; set; }

    /// <summary>
    /// How many times this has been round the loop.
    ///
    /// Worth counting: a request returned four times usually means the form is asking the wrong
    /// question rather than that the requester is being unhelpful.
    /// </summary>
    public int ReturnCount { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>The assignment created when this request was approved, closing the loop.</summary>
    public Guid? GrantedUserRoleId { get; set; }

    /// <summary>True when what is being asked for is marked sensitive, which tightens approval.</summary>
    public bool IsSensitive { get; set; }

    public bool IsPending => Status == AccessRequestStatus.Submitted;

    public bool IsDecided => Status is AccessRequestStatus.Approved or AccessRequestStatus.Rejected;
}
