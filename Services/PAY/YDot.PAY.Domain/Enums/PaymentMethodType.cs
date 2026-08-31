namespace YDot.PAY.Domain.Enums;

/// <summary>How the money moved.</summary>
public enum PaymentMethodType
{
    Card = 0,
    NetBanking = 1,

    /// <summary>Unified Payments Interface. The dominant method in India.</summary>
    Upi = 2,

    Wallet = 3,
    BankTransfer = 4,
    Cheque = 5,
    Cash = 6,
    DirectDebit = 7,
    Other = 8
}
