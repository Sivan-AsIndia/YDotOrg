namespace YDot.IAM.Domain.Enums;

/// <summary>
/// What kind of money a Currency row describes. Receipting, tax reporting and settlement
/// all treat these differently, so the distinction is stored rather than inferred from the
/// code.
/// </summary>
public enum CurrencyType
{
    /// <summary>A national currency with an ISO 4217 code.</summary>
    Fiat = 0,

    /// <summary>A digital asset. No ISO code, and usually more than two decimal places.</summary>
    Crypto = 1,

    /// <summary>Vouchers, loyalty points, in-kind valuations.</summary>
    Other = 2
}
