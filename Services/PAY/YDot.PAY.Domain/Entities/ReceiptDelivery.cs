using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// One attempt to get a receipt to the donor.
///
/// SEPARATE FROM THE RECEIPT because issuing and delivering are different events with different
/// failure modes. A receipt whose e-mail bounced is still validly issued - the donor is entitled
/// to it, and somebody needs to find another way to send it. Collapsing the two would make a
/// bounced e-mail look like an unissued receipt.
/// </summary>
public sealed class ReceiptDelivery : TenantEntity
{
    public Guid ReceiptId { get; set; }

    public Receipt Receipt { get; set; } = default!;

    /// <summary>How it was sent: Email, Sms, Post, Download.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Where it was sent - the address or number, recorded as used.</summary>
    public string Destination { get; set; } = string.Empty;

    public ReceiptDeliveryStatus Status { get; set; } = ReceiptDeliveryStatus.Pending;

    public DateTimeOffset AttemptedAtUtc { get; set; }

    public DateTimeOffset? DeliveredAtUtc { get; set; }

    /// <summary>The bounce or rejection reason, verbatim from the provider.</summary>
    public string? FailureReason { get; set; }

    /// <summary>The mail provider's own id, so a delivery can be traced on their side.</summary>
    public string? ProviderReference { get; set; }
}
