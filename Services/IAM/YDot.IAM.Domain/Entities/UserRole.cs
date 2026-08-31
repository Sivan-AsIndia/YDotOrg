using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// UserRoleMapping: the relationship between a Tenant user and a Tenant role.
///
/// A CUSTOMISED IdentityCore JOIN TABLE. It derives from
/// <see cref="IdentityUserRole{TKey}"/>, so <c>UserId</c> and <c>RoleId</c> come from the
/// base and UserManager.AddToRoleAsync writes through this entity. Everything below is the
/// customisation the brief asks for.
///
/// THE THIRD COLUMN IS THE POINT. The brief spells the shape out as UserId + RoleId +
/// TenantId, and TenantId is not redundant with the user and the role each having one — it
/// is what makes the composite foreign keys possible, so the DATABASE refuses a mapping that
/// pairs a user in TEN001 with a role in TEN002. Without it, cross-Tenant privilege
/// escalation would be one bad handler away rather than structurally impossible.
///
/// A NOTE ON THE KEY. <see cref="IdentityUserRole{TKey}"/> has a composite primary key of
/// (UserId, RoleId), which would allow a user to hold a role exactly once — correct — but
/// leaves nowhere to hang a revoked-then-regranted history. The EF configuration therefore
/// gives this table its own surrogate <see cref="Id"/> and demotes (TenantId, UserId, RoleId)
/// to a filtered unique index over the ACTIVE rows only. The result: one live assignment per
/// pair, and as many closed historical ones as the audit trail needs.
///
/// ONE USER, MANY ROLES. Many-to-many by design: the effective permission set is the union
/// of every active assignment, minus anything explicitly denied.
/// </summary>
public class UserRole : IdentityUserRole<Guid>, IAuditable, ITenantScoped
{
    /// <summary>
    /// Surrogate key. Present because assignments are kept rather than deleted when they end,
    /// so the same (UserId, RoleId) pair legitimately appears more than once over time.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    // ---- Audit, from IAuditable ---------------------------------------------------------

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public long Version { get; set; } = 1;

    // ---- Tenancy -------------------------------------------------------------------------

    /// <summary>
    /// Nullable, and it has to be. SuperAdmin (TenantId null) holds a Platform role
    /// (TenantId null), so their assignment row belongs to no Organisation either. A
    /// non-nullable column here would make the root user impossible to represent.
    ///
    /// It is also what lets the composite foreign keys below line up: a foreign key column
    /// must match the nullability of the principal key it points at, and both User.TenantId
    /// and Role.TenantId are nullable for exactly the same reason.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    // ---- Navigations ------------------------------------------------------------------------

    public User? User { get; set; }

    public Role? Role { get; set; }

    // ---- Assignment lifecycle ------------------------------------------------------------------

    public UserRoleAssignmentStatus Status { get; set; } = UserRoleAssignmentStatus.Active;

    /// <summary>
    /// Marks the role that represents this person primarily, when they hold several. Purely
    /// presentational — it changes which badge the directory shows, never what they may do.
    /// At most one per user.
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }

    public Guid AssignedByUserId { get; set; }

    /// <summary>When the assignment starts to count. Usually the moment it was made.</summary>
    public DateTimeOffset EffectiveFromUtc { get; set; }

    /// <summary>
    /// Null means permanent. A value here is what makes temporary access genuinely temporary:
    /// the assignment stops being effective on its own, with nothing to remember to undo.
    /// </summary>
    public DateTimeOffset? EffectiveToUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>Set when the assignment came out of an approved access request.</summary>
    public Guid? SourceAccessRequestId { get; set; }

    /// <summary>Free-text justification captured when the assignment was made.</summary>
    public string? Justification { get; set; }

    /// <summary>
    /// Whether this assignment actually counts right now: active status, and inside its
    /// effective window. The permission builder calls this rather than testing Status alone,
    /// so an expired assignment stops granting access without a job having to sweep it.
    /// </summary>
    public bool IsEffective(DateTimeOffset asOf) =>
        Status == UserRoleAssignmentStatus.Active
        && EffectiveFromUtc <= asOf
        && (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > asOf);
}
