namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>Single source of the current time so every stored timestamp is UTC.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
