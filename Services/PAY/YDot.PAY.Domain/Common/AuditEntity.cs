namespace YDot.PAY.Domain.Common;

/// <summary>
/// The mandatory audit columns and the optimistic concurrency version.
///
/// <c>PaymentDbContext.SaveChangesAsync</c> fills all five in, so no handler has to remember
/// them and none of them can be set by a caller. <see cref="Version"/> in particular is
/// server-owned: a request supplies the version it EXPECTS, and the context decides what the
/// version becomes.
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
