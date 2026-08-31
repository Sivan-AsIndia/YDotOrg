using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// A request to give money back.
///
/// IT IS A CASE, NOT AN ACTION, and that is the point. A refund needs a reason, a decision by
/// somebody other than the requester, and a record of what the gateway then did - so it has a
/// lifecycle of its own rather than being a method call that either works or does not.
///
/// PARTIAL REFUNDS ARE FIRST CLASS. A donor who gave 50,000 and wants 10,000 back leaves the
/// donation partially refunded and still receiptable for the remainder, which is why the amount
/// lives here and not just on the donation.
/// </summary>
public sealed class RefundCase : TenantEntity
{
    /// <summary>The reference support and the donor both quote.</summary>
    public string CaseReference { get; set; } = string.Empty;

    public Guid DonationId { get; set; }

    public Donation Donation { get; set; } = default!;

    public RefundStatus Status { get; set; } = RefundStatus.Requested;

    public RefundReason Reason { get; set; }

    /// <summary>Required when <see cref="Reason"/> is Other, and free text otherwise.</summary>
    public string? ReasonDetail { get; set; }

    /// <summary>How much is going back. Never more than the donation's refundable balance.</summary>
    public MoneyValue Amount { get; set; } = default!;

    public Guid RequestedByUserId { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>
    /// Who decided.
    ///
    /// MUST NOT BE THE REQUESTER - money leaving the organisation needs two people, which is the
    /// same segregation-of-duties rule campaigns use for approvals.
    /// </summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecisionNote { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>The gateway's own refund id, once submitted.</summary>
    public string? GatewayRefundReference { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? GatewayFailureReason { get; set; }

    /// <summary>
    /// Whether the receipt was reissued for the reduced amount.
    ///
    /// A refund without a corrected receipt leaves the donor holding a tax document for money
    /// they no longer gave, which is the compliance problem this flag exists to surface.
    /// </summary>
    public bool ReceiptCorrected { get; set; }

    /// <summary>True while the case still needs somebody to act.</summary>
    public bool IsOpen => Status is RefundStatus.Requested
        or RefundStatus.Approved
        or RefundStatus.Processing;

    /// <summary>The independence rule: whoever asked for the money back cannot approve it.</summary>
    public bool CanBeDecidedBy(Guid userId) => RequestedByUserId != userId;
}
