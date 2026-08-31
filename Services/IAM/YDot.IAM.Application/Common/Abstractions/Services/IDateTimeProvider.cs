namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// The clock, behind an interface so lockout windows, token expiry and access windows are
/// all driven from one place rather than scattered DateTimeOffset.UtcNow calls.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayUtc { get; }
}
