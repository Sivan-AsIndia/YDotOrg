namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.4: Draft|Submitted|Approved|Rejected|Withdrawn|Expired.</summary>
public enum AccessRequestStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    Expired = 5,

    /// <summary>
    /// Sent back to the requester for more information.
    ///
    /// DISTINCT FROM REJECTED, and the distinction earns its place. A rejection is a decision:
    /// the answer is no, and raising it again means starting over. A return is not a decision at
    /// all - the approver cannot answer yet, usually because the justification does not say what
    /// the access is for. Collapsing the two would mean every "I need more detail" arrived as a
    /// refusal, which is both discouraging and inaccurate.
    ///
    /// A returned request goes back to Draft's rules: the requester edits it and submits again,
    /// keeping its number and its history rather than appearing as a second request.
    /// </summary>
    Returned = 6
}
