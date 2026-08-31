namespace YDot.PAY.Application.Common.Models;

/// <summary>
/// An amount as the client receives it.
///
/// IT CARRIES A PRE-FORMATTED <see cref="Display"/> STRING alongside the raw figure, which is
/// unusual enough to explain. Formatting money correctly needs the currency's symbol, its
/// position and its decimal places - all of which live on the IAM currency master. Rendering it
/// in the browser would mean every client fetching that master and reimplementing the rule, and
/// a receipt total that disagrees with the screen by a rounding place is a support call.
/// </summary>
public sealed record MoneyResponse(decimal Amount, string CurrencyCode, string Display)
{
    /// <summary>
    /// A plain rendering for the common case.
    ///
    /// Used where no currency master row is to hand. The symbol-aware version comes from the
    /// mapper, which has the currency row loaded.
    /// </summary>
    public static MoneyResponse Plain(decimal amount, string currencyCode) =>
        new(amount, currencyCode,
            $"{amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {currencyCode}");
}
