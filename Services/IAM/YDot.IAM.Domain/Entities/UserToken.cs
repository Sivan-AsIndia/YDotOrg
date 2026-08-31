using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// The IdentityCore token store, deriving from <see cref="IdentityUserToken{TKey}"/> so
/// <c>UserId</c>, <c>LoginProvider</c>, <c>Name</c> and <c>Value</c> come from the base.
///
/// WHAT ACTUALLY USES IT. The framework token providers write here: the authenticator key
/// when enrolment goes through UserManager, external provider access tokens, and the
/// recovery-code set when generated the framework way.
///
/// YDot mostly does not use those paths — it has richer tables of its own
/// (<see cref="MfaMethod"/>, <see cref="RecoveryCode"/>, <see cref="RecoveryToken"/>) that
/// carry expiry, attempt counts, revocation reasons and per-Tenant scoping, none of which
/// this flat key-value shape can express. The table is present so IdentityCore is complete
/// and so any framework path that reaches for it finds it, rather than failing at runtime on
/// a table that was left out.
///
/// Values here are treated as secrets and encrypted at rest by the same value converter that
/// protects the authenticator secret.
/// </summary>
public class UserToken : IdentityUserToken<Guid>, ITenantScoped
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

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Null means it does not expire. The framework has no such concept; YDot does.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsUsable(DateTimeOffset asOf) => !ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > asOf;
}
