using YDot.PAY.Application.Common.Models;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Refunds.DTOs;

// =====================================================================================
// Refunds
// =====================================================================================

/// <summary>
/// Raising a refund.
///
/// THE AMOUNT IS EXPLICIT because partial refunds are first class: a donor who gave 50,000 and
/// wants 10,000 back leaves the donation partially refunded and still receiptable for the rest.
/// </summary>
public sealed record RequestRefundRequest(
    decimal Amount,
    RefundReason Reason,
    string? ReasonDetail = null);

/// <summary>
/// Deciding a refund.
///
/// The handler REFUSES THE PERSON WHO RAISED IT, whatever permissions they hold. Money leaving
/// the organisation needs two people, and that is a per-record rule a permission cannot express.
/// </summary>
public sealed record DecideRefundRequest(long ExpectedVersion, string? Note = null);

/// <summary>Rejecting a refund. A reason is mandatory - somebody asked and deserves an answer.</summary>
public sealed record RejectRefundRequest(long ExpectedVersion, string Reason);

/// <summary>A refund as a donation detail screen shows it inline.</summary>
public sealed record RefundCaseSummaryResponse(
    Guid Id,
    string CaseReference,
    RefundStatus Status,
    string StatusDescription,
    MoneyResponse Amount,
    RefundReason Reason,
    DateTimeOffset RequestedAtUtc);

/// <summary>One row of the refund register.</summary>
public sealed record RefundCaseListItemResponse(
    Guid Id,
    string CaseReference,
    Guid DonationId,
    string DonationReference,
    string DonorName,
    MoneyResponse Amount,
    MoneyResponse DonationAmount,
    RefundStatus Status,
    string StatusDescription,
    RefundReason Reason,
    string ReasonDescription,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    bool ReceiptCorrected,
    long Version);

/// <summary>The full refund case - SCR-PAY-006.</summary>
public sealed record RefundCaseDetailResponse(
    Guid Id,
    Guid TenantId,
    string CaseReference,
    Guid DonationId,
    string DonationReference,
    string DonorName,
    string DonorEmail,
    MoneyResponse Amount,
    MoneyResponse DonationAmount,
    MoneyResponse RefundableBalance,
    RefundStatus Status,
    string StatusDescription,
    RefundReason Reason,
    string ReasonDescription,
    string? ReasonDetail,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    string? RejectionReason,
    string? GatewayRefundReference,
    DateTimeOffset? ProcessedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? GatewayFailureReason,

    /// <summary>
    /// Whether the receipt was reissued for the reduced amount.
    ///
    /// Surfaced because a refund without a corrected receipt leaves the donor holding a tax
    /// document for money they no longer gave.
    /// </summary>
    bool ReceiptCorrected,

    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>One line of the refund export.</summary>
public sealed record RefundExportRow(
    string CaseReference,
    string DonationReference,
    string DonorName,
    string Amount,
    string Currency,
    string Status,
    string Reason,
    string? ReasonDetail,
    string RequestedAt,
    string? DecidedAt,
    string? CompletedAt,
    string ReceiptCorrected);

/// <summary>Filter for the refund register.</summary>
public sealed class RefundSearchFilter : PaginationRequest
{
    public RefundStatus? Status { get; set; }

    public RefundReason? Reason { get; set; }

    public Guid? DonationId { get; set; }

    public DateTimeOffset? RequestedFromUtc { get; set; }

    public DateTimeOffset? RequestedToUtc { get; set; }

    /// <summary>Requested, Approved or Processing - the cases still needing somebody.</summary>
    public bool? OpenOnly { get; set; }

    /// <summary>Completed refunds whose receipt has not been corrected. A compliance queue.</summary>
    public bool? AwaitingReceiptCorrection { get; set; }
}

// =====================================================================================
// Chargebacks
// =====================================================================================

/// <summary>Assigning a chargeback case to somebody. A case with no owner is one nobody works.</summary>
public sealed record AssignChargebackRequest(long ExpectedVersion, Guid AssignToUserId);

/// <summary>Submitting evidence to contest a chargeback.</summary>
public sealed record SubmitChargebackEvidenceRequest(
    long ExpectedVersion,
    string EvidenceSummary,

    /// <summary>Where the supporting documents live, comma separated.</summary>
    string? EvidenceDocumentUrls = null);

/// <summary>Recording the bank's decision, or conceding without contest.</summary>
public sealed record ResolveChargebackRequest(
    long ExpectedVersion,
    ChargebackStatus Outcome,
    string ResolutionNote);

/// <summary>A chargeback as a donation detail screen shows it inline.</summary>
public sealed record ChargebackCaseSummaryResponse(
    Guid Id,
    string CaseReference,
    ChargebackStatus Status,
    string StatusDescription,
    MoneyResponse DisputedAmount,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? EvidenceDueAtUtc,
    bool IsOverdue);

/// <summary>One row of the chargeback register.</summary>
public sealed record ChargebackCaseListItemResponse(
    Guid Id,
    string CaseReference,
    Guid DonationId,
    string DonationReference,
    string DonorName,
    MoneyResponse DisputedAmount,
    MoneyResponse? ChargebackFee,
    ChargebackStatus Status,
    string StatusDescription,
    string? ReasonCode,
    string? ReasonDescription,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? EvidenceDueAtUtc,

    /// <summary>
    /// Days left to submit evidence. Negative when the deadline has passed.
    ///
    /// Computed by the server so every client shows the same number, and so the queue can be
    /// sorted by urgency without each one working out the arithmetic.
    /// </summary>
    int? DaysUntilEvidenceDue,

    bool IsOverdue,
    Guid? AssignedToUserId,
    long Version);

/// <summary>The full chargeback case - SCR-PAY-008.</summary>
public sealed record ChargebackCaseDetailResponse(
    Guid Id,
    Guid TenantId,
    string CaseReference,
    Guid DonationId,
    string DonationReference,
    string DonorName,
    string DonorEmail,
    MoneyResponse DisputedAmount,
    MoneyResponse? ChargebackFee,
    ChargebackStatus Status,
    string StatusDescription,
    string? GatewayDisputeReference,
    string? ReasonCode,
    string? ReasonDescription,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? EvidenceDueAtUtc,
    int? DaysUntilEvidenceDue,
    bool IsOverdue,
    DateTimeOffset? EvidenceSubmittedAtUtc,
    Guid? EvidenceSubmittedByUserId,
    string? EvidenceSummary,
    IReadOnlyList<string> EvidenceDocumentUrls,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionNote,
    Guid? AssignedToUserId,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>Filter for the chargeback register.</summary>
public sealed class ChargebackSearchFilter : PaginationRequest
{
    public ChargebackStatus? Status { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public Guid? DonationId { get; set; }

    /// <summary>Open cases past their evidence deadline. The most urgent queue in the module.</summary>
    public bool? OverdueOnly { get; set; }

    public bool? OpenOnly { get; set; }
}
