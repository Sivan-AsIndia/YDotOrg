namespace YDot.PAY.Domain.Enums;

/// <summary>
/// The issue state of a receipt. Mirrors the controlled catalogue the receipt register screen
/// uses, so the API and the UI name the same states.
/// </summary>
public enum ReceiptStatus
{
    Draft = 0,
    Submitted = 1,
    PendingReview = 2,

    /// <summary>Issued to the donor. A tax document from here on.</summary>
    Issued = 3,

    /// <summary>Superseded by a corrected version. The original stays for the audit trail.</summary>
    Corrected = 4,

    /// <summary>Cancelled. Never valid.</summary>
    Voided = 5
}
