using YDot.PAY.Application.Common.Models;

namespace YDot.PAY.Application.Features.Receipts.DTOs;

/// <summary>
/// One line of the Receipt Register as the YDot Donation Flow document draws it (Fig 5).
///
/// IT IS NOT ALWAYS A RECEIPT, AND THAT IS THE DIFFICULT PART OF THIS SCREEN. The document says
/// "whether a payment ends in Success or Fail, the result is recorded and shown on the Payment
/// Receipt page", and its screenshot shows failed rows sitting beside successful ones.
///
/// A TAX RECEIPT IS NEVER ISSUED FOR A FAILED PAYMENT. Creating a Receipt row for money that was
/// refused would put a numbered, exemption-bearing tax document against a donation that never
/// happened - which a donor could claim on and an auditor would treat as fraud. So the register
/// is a UNION rather than a table: issued receipts supply the Success lines, and failed donation
/// intents supply the Failed ones. Only the successful half has a receipt number, which is why
/// <see cref="ReceiptNumber"/> is nullable and the failed half quotes its donation reference
/// instead.
/// </summary>
public sealed record ReceiptRegisterRowResponse(
    /// <summary>The receipt id for a Success line; the intent id for a Failed one.</summary>
    Guid Id,

    /// <summary>REC-... for an issued receipt. Null when the payment failed.</summary>
    string? ReceiptNumber,

    /// <summary>What the row is quoted by: the receipt number, or the donation reference.</summary>
    string Reference,

    DateTimeOffset? ReceiptDateUtc,

    /// <summary>The donor AS PRINTED, for a receipt. As recorded on the intent, for a failure.</summary>
    string DonorSnapshot,

    MoneyResponse Amount,

    /// <summary>Success or Failed - the document's own two words.</summary>
    string Status,

    string? CampaignOrFundName,

    /// <summary>Null for a failed payment: there is no document to open.</summary>
    string? DocumentUrl,

    /// <summary>Whether a copy reached the donor. "Not sent" for the failed half.</summary>
    string DeliveryState);

/// <summary>
/// The four cards across the top of the register.
///
/// COUNTED OVER THE WHOLE SCOPE, NOT OVER THE PAGE. The register pages at eight rows; totals that
/// counted the page would read "Total Receipts 8" for an organisation with four hundred.
/// </summary>
public sealed record ReceiptRegisterSummaryResponse(
    int TotalReceipts,

    /// <summary>
    /// SUCCESSFUL MONEY ONLY. A failed payment moved nothing, so adding its amount here would
    /// overstate what the charity received - which is the one number on this screen somebody
    /// might copy into a report.
    /// </summary>
    MoneyResponse TotalAmount,

    int Successful,
    int Failed);

/// <summary>The register: its rows and its totals, in one answer.</summary>
public sealed record ReceiptRegisterResponse(
    PagedResponse<ReceiptRegisterRowResponse> Rows,
    ReceiptRegisterSummaryResponse Summary,
    IReadOnlyList<string> PermittedActions);

/// <summary>Query string of the Receipt Register.</summary>
public sealed class ReceiptRegisterFilter : PaginationRequest
{
    /// <summary>Success / Failed. Unset returns both, which is the register as documented.</summary>
    public string? Status { get; set; }

    public Guid? CampaignId { get; set; }

    public DateTimeOffset? FromUtc { get; set; }

    public DateTimeOffset? ToUtc { get; set; }
}
