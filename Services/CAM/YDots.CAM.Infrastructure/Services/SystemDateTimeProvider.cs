using YDots.CAM.Application.Common.Abstractions.Services;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// The real clock.
///
/// Everything is UTC. A campaign start date has to mean the same instant whichever server
/// evaluates it, and a local time on a database shared by three services is a bug waiting for a
/// deployment to another region.
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}
