namespace YDot.IAM.Domain.Common;

/// <summary>
/// For the entities that are normally Tenant-owned but have a legitimate global form:
/// <c>User</c>, <c>Role</c>, and the IdentityCore join tables that hang off them.
///
/// WHY THIS IS NOT JUST <see cref="ITenantOwned"/> WITH A NULLABLE COLUMN. The brief is
/// unambiguous that SuperAdmin is a root user and not a Tenant user:
///
/// <code>
/// SuperAdmin
///     UserId   = U001
///     TenantId = NULL
///     Global/Root scope = true
/// </code>
///
/// and that "selecting a Tenant must NOT modify SuperAdmin persistent User.TenantId".
/// A non-nullable TenantId would force SuperAdmin to be given a home Organisation, and the
/// moment that exists somebody will write a query that treats them as belonging to it.
/// Null is the honest representation: this row belongs to no Organisation.
///
/// THE FILTER IS DIFFERENT, AND DELIBERATELY SO. <see cref="ITenantOwned"/> rows are filtered
/// to "TenantId == current". Rows marked with this interface are filtered to
/// "TenantId == current OR TenantId == null", so the global SuperAdmin record stays
/// reachable while a Tenant is selected. That widening is why the interface is separate: it
/// is a real hole in the isolation, it is only a handful of tables wide, and it should be
/// obvious at the point of use rather than hidden behind a nullable column on forty entities.
/// </summary>
public interface ITenantScoped
{
    /// <summary>The owning Organisation, or null for a global/root row.</summary>
    Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/>, where <c>Guid.Empty</c> stands for "the
    /// platform". Maintained automatically by the DbContext on every save — never set it by
    /// hand.
    ///
    /// WHY THIS COLUMN EXISTS. EF Core makes every property of a key REQUIRED. So an
    /// alternate key of (TenantId, Id) — which is what a Tenant-scoped composite foreign key
    /// has to point at — silently turns <see cref="TenantId"/> into a NOT NULL column, and
    /// SuperAdmin becomes impossible to store. That is not a hypothetical: it is exactly what
    /// the first generated migration did.
    ///
    /// Mirroring null onto <c>Guid.Empty</c> resolves the conflict without giving up either
    /// half. <see cref="TenantId"/> stays nullable and keeps its honest domain meaning, while
    /// this column is non-null and can carry the alternate key. The composite foreign keys on
    /// <c>iam_user_roles</c> then point at (TenantKey, Id), so the DATABASE refuses a row
    /// pairing a user in TEN001 with a role in TEN002 — and equally refuses pairing the
    /// global SuperAdmin with a Tenant role, because Guid.Empty groups the platform rows
    /// together as their own scope.
    /// </summary>
    Guid TenantKey { get; set; }

    /// <summary>The root boundary. Always present, even on a global row.</summary>
    Guid BusinessUnitId { get; set; }
}
