using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A currency a donation can be denominated in.
///
/// <see cref="GlobalMasterEntity.Code"/> holds the ISO 4217 code (INR, GBP). The formatting
/// and rounding columns are what let a receipt, a payment request and a finance report agree
/// on the same number: they all read the rule from here rather than each applying their own.
/// </summary>
public sealed class Currency : GlobalMasterEntity
{
    /// <summary>ISO 4217 numeric code. Null for crypto and internal units, which have none.</summary>
    public int? NumericCode { get; set; }

    public CurrencyType CurrencyType { get; set; } = CurrencyType.Fiat;

    /// <summary>The symbol as it should be rendered, for example the rupee sign.</summary>
    public string? Symbol { get; set; }

    public SymbolPosition SymbolPosition { get; set; } = SymbolPosition.Prefix;

    /// <summary>
    /// A .NET-style custom format, for example <c>#,##0.00</c>. Null falls back to the
    /// culture default for <see cref="DecimalPlaces"/>.
    /// </summary>
    public string? DisplayFormat { get; set; }

    /// <summary>
    /// How many places the currency actually subdivides into. Two for most, zero for JPY,
    /// three for KWD, eight for Bitcoin - which is why this is a column and not a constant.
    /// </summary>
    public int DecimalPlaces { get; set; } = 2;

    /// <summary>What the smallest unit is called: "paisa", "cent", "satoshi".</summary>
    public string? MinorUnitName { get; set; }

    public RoundingMode RoundingMode { get; set; } = RoundingMode.HalfUp;

    /// <summary>
    /// The increment amounts are rounded to, where it is coarser than one minor unit - a
    /// currency whose smallest circulating coin is 5 has a step of 0.05.
    /// </summary>
    public decimal? RoundingStep { get; set; }

    /// <summary>True where the currency has no fractional part at all, such as JPY.</summary>
    public bool IsZeroDecimal => DecimalPlaces == 0;
}
