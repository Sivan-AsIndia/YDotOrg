using YDots.DON.Application.Common.Abstractions.Services;

namespace YDots.DON.Infrastructure.Services;

/// <summary>The real clock. Always UTC, so a stored timestamp never depends on server locale.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
