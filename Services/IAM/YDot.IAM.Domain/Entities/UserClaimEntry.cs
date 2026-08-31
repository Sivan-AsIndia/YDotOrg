using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An extra claim attached directly to one user, over and above whatever their roles carry.
///
/// A CUSTOMISED IdentityCore TABLE, deriving from <see cref="IdentityUserClaim{TKey}"/>, so
/// <c>Id</c>, <c>UserId</c>, <c>ClaimType</c> and <c>ClaimValue</c> come from the base and
/// UserManager.AddClaimAsync writes through this entity. The tenancy and the grant
/// bookkeeping below are the customisation.
///
/// This is the escape hatch for the one-off: a single person who needs one extra capability
/// that does not justify inventing a role. Use it sparingly — access granted here is
/// invisible on the role screens, which is exactly why it is folded into the effective-access
/// preview and into every access review.
/// </summary>
public class UserClaimEntry : IdentityUserClaim<Guid>, ITenantScoped
{
    /// <summary>
    /// Nullable so the row can belong to the global SuperAdmin or a Platform role, which
    /// belong to no Organisation. Matches the nullability of User.TenantId and Role.TenantId.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    public User? User { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; }

    public Guid GrantedByUserId { get; set; }

    /// <summary>Null means permanent.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Why this person needed something their roles did not give them.</summary>
    public string? Justification { get; set; }

    public bool IsEffective(DateTimeOffset asOf) => !ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > asOf;
}
