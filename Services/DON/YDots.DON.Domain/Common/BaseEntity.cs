namespace YDots.DON.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Every identifier in YDot is a UUID (Guid).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
