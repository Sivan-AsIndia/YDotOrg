namespace YDot.IAM.Domain.Common;

/// <summary>
/// Convenience base for entities that carry the mandatory audit columns and the optimistic
/// concurrency version. The DbContext fills these in, so no handler has to remember them.
///
/// This is just <see cref="BaseEntity"/> plus <see cref="IAuditable"/> implemented once.
/// Entities that cannot use it — <c>User</c> and <c>Role</c>, which derive from the
/// IdentityCore base classes instead — implement <see cref="IAuditable"/> directly and are
/// stamped by exactly the same code.
/// </summary>
public abstract class AuditEntity : BaseEntity, IAuditable
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    /// <summary>Optimistic concurrency token. Starts at 1 and increases on every update.</summary>
    public long Version { get; set; } = 1;
}
