namespace YDots.DON.Domain.Common;

/// <summary>
/// Base type for entities that carry the mandatory audit columns and the optimistic
/// concurrency version from section 3 of the Donors developer contract.
/// The DbContext fills these in, so no handler has to remember them.
/// </summary>
public abstract class AuditEntity : BaseEntity
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    /// <summary>Optimistic concurrency token. Starts at 1 and increases on every update.</summary>
    public long Version { get; set; } = 1;
}
