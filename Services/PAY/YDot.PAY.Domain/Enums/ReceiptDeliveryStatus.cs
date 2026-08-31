namespace YDot.PAY.Domain.Enums;

/// <summary>Whether the receipt actually reached the donor. Separate from whether it was issued.</summary>
public enum ReceiptDeliveryStatus
{
    NotSent = 0,
    Pending = 1,
    Delivered = 2,

    /// <summary>Bounced or rejected. The receipt is still validly issued; it just did not arrive.</summary>
    Failed = 3
}
