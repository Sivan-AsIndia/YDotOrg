namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The narrowing scopes a Tenant user can be given inside their own Organisation.
///
/// THIS ENUM IS A CROSS-SERVICE CONTRACT. IAM writes one <c>data_scope</c> claim per
/// assignment as "{ScopeType}:{ScopeValue}", and DON's <c>AccessScope</c> record parses
/// exactly that shape. Renaming a member here breaks the Donors service silently, so add
/// rather than rename.
///
/// These narrow WITHIN a Tenant. They are not, and can never be, a way out of one — the
/// Tenant boundary is enforced above this by the query filter and the token's tenant_id.
/// </summary>
public enum DataScopeType
{
    /// <summary>Everything inside the Organisation. The default when no scope is assigned.</summary>
    Organisation = 0,
    Geography = 1,
    Campaign = 2,
    Warehouse = 3,
    Queue = 4,

    /// <summary>Only records assigned to this user.</summary>
    Assignment = 5,

    /// <summary>One explicitly listed record.</summary>
    ExplicitRecord = 6
}
