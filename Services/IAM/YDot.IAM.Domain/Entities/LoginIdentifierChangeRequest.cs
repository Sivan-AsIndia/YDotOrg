using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// IAM-USR-05: a request to change the e-mail or username somebody signs in with.
///
/// WHY THIS IS NOT JUST AN UPDATE. The login identifier is the thing password recovery is
/// addressed to. Letting it be edited in place would mean anybody with a briefly unattended
/// session could point recovery at their own mailbox and own the account permanently. So the
/// change is a request with its own lifecycle: the new address has to be proved by a code
/// sent to it, the old address is notified, and on a privileged account a second person has
/// to approve.
///
/// UNIQUENESS IS STILL PER TENANT. The new value is checked against the same
/// (TenantId, NormalizedEmail) index as everything else, so it may already exist in another
/// Organisation without conflict.
/// </summary>
public class LoginIdentifierChangeRequest : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>True when the e-mail is changing, false when the username is.</summary>
    public bool IsEmailChange { get; set; }

    /// <summary>Kept so the trail shows what it was, even after the change lands.</summary>
    public string CurrentValue { get; set; } = string.Empty;

    public string RequestedValue { get; set; } = string.Empty;

    public string NormalizedRequestedValue { get; set; } = string.Empty;

    public LoginIdentifierChangeStatus Status { get; set; } = LoginIdentifierChangeStatus.Draft;

    public DateTimeOffset RequestedAtUtc { get; set; }

    public Guid RequestedByUserId { get; set; }

    public string? Reason { get; set; }

    /// <summary>The challenge sent to the NEW address to prove the person can receive there.</summary>
    public Guid? VerificationChallengeId { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    /// <summary>
    /// Set once the old address has been told. Not a veto, just a warning shot: it is what
    /// gives the real owner a chance to notice and object.
    /// </summary>
    public DateTimeOffset? PreviousOwnerNotifiedAtUtc { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? RejectedAtUtc { get; set; }

    public Guid? RejectedByUserId { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>When the new identifier actually took effect on the user row.</summary>
    public DateTimeOffset? AppliedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// True when a second person has to approve. Set for privileged accounts, where
    /// self-service would let one compromised session complete the whole takeover.
    /// </summary>
    public bool RequiresApproval { get; set; }

    public bool IsActionable(DateTimeOffset asOf) =>
        Status is LoginIdentifierChangeStatus.PendingVerification or LoginIdentifierChangeStatus.PendingApproval
        && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > asOf);
}
