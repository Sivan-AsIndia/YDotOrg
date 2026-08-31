namespace YDot.PAY.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Every identifier in YDot is a UUID (Guid), generated
/// client-side so an aggregate has an identity before it is ever saved.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
