namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The <c>scope</c> claim in the JWT, and the single most important authorisation fact
/// about a caller.
///
/// <c>Global</c> is SuperAdmin: unrestricted, may select any Tenant, and is NOT itself a
/// Tenant user. <c>Tenant</c> is everybody else: bound to exactly one Organisation for
/// the whole life of the token.
///
/// Selecting an Organisation as SuperAdmin sets the token's <c>tenant_id</c> and
/// <c>tenant_mode</c>, but never changes this claim and never writes to the SuperAdmin's
/// persistent <c>User.TenantId</c>, which stays NULL forever.
/// </summary>
public enum AccessScopeType
{
    /// <summary>Bound to one Organisation. The normal case.</summary>
    Tenant = 0,

    /// <summary>Root/global user. SuperAdmin only.</summary>
    Global = 1
}
