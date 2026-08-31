namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The Organisation/Tenant lifecycle from section 8 of the brief, modelled explicitly.
///
/// The brief is emphatic that this must NOT collapse into <c>IsApproved = true/false</c>:
/// the difference between "we invited them and heard nothing", "they are still filling the
/// form in", "they submitted and nobody has looked yet" and "we turned them down" is
/// operationally important, and a boolean throws all four away.
///
/// <code>
/// Invited
///    |
///    v
/// InvitationAccepted
///    |
///    v
/// ProfileIncomplete
///    |
///    v
/// Submitted
///    |
///    v
/// UnderReview
///    |
///    +----> Rejected ----> Resubmitted ---+
///    |                                    |
///    v                                    |
/// Approved  &lt;-----------------------------+
///    |
///    v
/// Active
/// </code>
///
/// <c>Suspended</c> and <c>Archived</c> are reachable from <c>Active</c> and are terminal
/// for day-to-day use; only <c>Suspended</c> can return to <c>Active</c>.
///
/// The legal transitions live in one place — <c>Tenant.TransitionTo</c> — so no handler can
/// invent a shortcut such as Invited -> Active.
/// </summary>
public enum TenantStatus
{
    /// <summary>Created by SuperAdmin; the TenantAdmin invitation has been sent.</summary>
    Invited = 0,

    /// <summary>The TenantAdmin clicked the link and set a password.</summary>
    InvitationAccepted = 1,

    /// <summary>Account is live but the Organisation profile is not finished.</summary>
    ProfileIncomplete = 2,

    /// <summary>The TenantAdmin submitted the profile and documents for approval.</summary>
    Submitted = 3,

    /// <summary>SuperAdmin has picked the submission up.</summary>
    UnderReview = 4,

    /// <summary>Turned down with a reason. The TenantAdmin may correct and resubmit.</summary>
    Rejected = 5,

    /// <summary>Sent back in after a rejection. Distinct from Submitted so the review
    /// queue can show that this one has been round the loop before.</summary>
    Resubmitted = 6,

    /// <summary>SuperAdmin approved it. Activation is the separate final step.</summary>
    Approved = 7,

    /// <summary>Fully operational. This is the only status in which Tenant users may sign in.</summary>
    Active = 8,

    /// <summary>Temporarily blocked. Sign-in is refused; data is retained. Can return to Active.</summary>
    Suspended = 9,

    /// <summary>Retired. Read-only for SuperAdmin, invisible to everybody else. Terminal.</summary>
    Archived = 10
}
