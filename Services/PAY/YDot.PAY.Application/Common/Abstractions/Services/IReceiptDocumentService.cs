using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// Renders a receipt into a document and delivers it.
///
/// SEPARATE FROM ISSUING, because they fail differently and independently. A receipt is issued
/// the moment it is numbered and recorded; rendering a PDF and getting it into an inbox are
/// later steps that can fail without making the receipt any less valid. Collapsing them would
/// make a bounced e-mail look like an unissued receipt.
/// </summary>
public interface IReceiptDocumentService
{
    /// <summary>Renders the receipt and returns where the document was stored.</summary>
    Task<ReceiptDocumentResult> RenderAsync(Receipt receipt, CancellationToken cancellationToken);

    /// <summary>Sends an issued receipt to the donor.</summary>
    Task<ReceiptDeliveryResult> DeliverAsync(
        Receipt receipt, string channel, string destination, CancellationToken cancellationToken);
}

/// <summary>Where the rendered document ended up, or why it could not be produced.</summary>
public sealed record ReceiptDocumentResult(bool Succeeded, string? DocumentUrl, string? FailureReason);

/// <summary>Whether the receipt reached the donor.</summary>
public sealed record ReceiptDeliveryResult(
    bool Succeeded, string? ProviderReference, string? FailureReason);
