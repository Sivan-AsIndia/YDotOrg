using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// Grants one <see cref="Permission"/> to one <see cref="Role"/>.
///
/// Tenant-owned, even though Permission is global. That is the join that makes the model
/// work: every Organisation draws from the same global catalogue of capabilities, but the
/// decision about which of them a given role carries belongs to the Organisation and must
/// never leak between them.
/// </summary>
public class RolePermission : TenantEntity
{
    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }

    public Permission? Permission { get; set; }

    /// <summary>
    /// Denormalised copy of the permission code. It saves a join on the sign-in path, where
    /// the whole permission set has to be read to build the token, and that path runs on
    /// every single authentication.
    /// </summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>
    /// An explicit deny. Deny always beats allow when the effective set is computed, so a
    /// single permission can be carved out of a broad role without unpicking the role.
    /// </summary>
    public bool IsDenied { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; }

    public Guid GrantedByUserId { get; set; }

    /// <summary>Null means the grant does not expire. Set for temporary elevation.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public string? Notes { get; set; }

    public bool IsEffective(DateTimeOffset asOf) =>
        !IsDenied && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > asOf);
}
