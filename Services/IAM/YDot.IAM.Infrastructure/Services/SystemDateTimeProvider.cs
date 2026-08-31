using YDot.IAM.Application.Common.Abstractions.Services;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// The clock. Registered as a singleton because it holds no state.
///
/// Everything time-dependent goes through here rather than calling DateTimeOffset.UtcNow
/// directly, so lockout windows, token expiry and access windows all read the same clock.
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}
