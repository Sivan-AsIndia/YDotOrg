using System.Globalization;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Settings;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// The real clock, plus the financial-year rule.
///
/// EVERYTHING IS UTC. A donation timestamp decides which financial year its receipt falls in,
/// and a local time on a database shared by four services is a receipt numbered into the wrong
/// year the first time somebody deploys to another region.
/// </summary>
public sealed class SystemDateTimeProvider(IOptions<PaymentSettings> paymentSettings) : IDateTimeProvider
{
    private readonly PaymentSettings _settings = paymentSettings.Value;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// The financial year a moment falls in, as "2026-27".
    ///
    /// THE START MONTH IS CONFIGURED, not assumed. India's financial year begins in April, the
    /// UK's in April on a different day, and much of the world's in January - and a receipt
    /// numbered into the wrong year is a document the donor cannot use.
    ///
    /// A JANUARY START IS HANDLED SEPARATELY because "2026-27" would be wrong for it: where the
    /// financial year and the calendar year coincide the correct label is simply "2026", and a
    /// hyphenated span would imply a boundary that does not exist.
    /// </summary>
    public string FinancialYearFor(DateTimeOffset moment)
    {
        var startMonth = _settings.FinancialYearStartMonth is >= 1 and <= 12
            ? _settings.FinancialYearStartMonth
            : 4;

        var utc = moment.ToUniversalTime();

        if (startMonth == 1)
        {
            return utc.Year.ToString(CultureInfo.InvariantCulture);
        }

        // Before the start month the moment belongs to the year that began LAST calendar year.
        var startYear = utc.Month >= startMonth ? utc.Year : utc.Year - 1;

        var endYearSuffix = (startYear + 1) % 100;

        return string.Create(
            CultureInfo.InvariantCulture, $"{startYear}-{endYearSuffix:D2}");
    }
}
