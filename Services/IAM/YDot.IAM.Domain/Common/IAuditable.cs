namespace YDot.IAM.Domain.Common;

/// <summary>
/// The mandatory audit columns and the optimistic concurrency version, as an interface
/// rather than only a base class.
///
/// WHY AN INTERFACE. <c>User</c> and <c>Role</c> derive from <c>IdentityUser&lt;Guid&gt;</c>
/// and <c>IdentityRole&lt;Guid&gt;</c>, because the brief requires the real IdentityCore
/// tables customised for this application. C# has single inheritance, and those base classes
/// already supply <c>Id</c>, so those two entities cannot also extend
/// <see cref="AuditEntity"/>.
///
/// Stamping off this interface instead means <c>IamDbContext.SaveChangesAsync</c> fills in
/// the audit columns and bumps the version for every entity that carries them — the
/// Identity-derived ones and the plain ones alike — with one loop and no special cases.
/// <see cref="AuditEntity"/> simply becomes the convenience base for everything that is free
/// to use it.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; set; }

    Guid CreatedByUserId { get; set; }

    DateTimeOffset? UpdatedAtUtc { get; set; }

    Guid? UpdatedByUserId { get; set; }

    /// <summary>Optimistic concurrency token. Starts at 1 and increases on every update.</summary>
    long Version { get; set; }
}
