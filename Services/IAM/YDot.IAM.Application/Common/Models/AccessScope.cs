using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// The effective data scope of the caller, handed to every read service so no repository has
/// to reach for HttpContext.
///
/// TWO LAYERS, AND THEY ARE NOT THE SAME THING.
///
/// <code>
/// Scope        = Global | Tenant     which Organisations can I reach at all?
/// DataScopes   = Campaign:x, ...     within one Organisation, which records?
/// </code>
///
/// The first is the isolation boundary and is decided by the token. The second only ever
/// narrows, and no value of it can widen the first. Getting that ordering wrong is how
/// cross-tenant leaks happen, so the two are separate fields rather than one list.
///
/// THE CLAIM FORMAT IS A CROSS-SERVICE CONTRACT. IAM writes one <c>data_scope</c> claim per
/// assignment as "{ScopeType}:{ScopeValue}", where ScopeType is a member of
/// <see cref="DataScopeType"/>. The Donors service parses exactly this shape, so the
/// spellings here have to keep matching.
/// </summary>
public sealed record AccessScope(
    Guid BusinessUnitId,
    Guid? TenantId,
    Guid UserId,
    AccessScopeType Scope,
    IReadOnlyList<string> DataScopes)
{
    public const string OrganisationScopeType = "Organisation";
    public const string GeographyScopeType = "Geography";
    public const string CampaignScopeType = "Campaign";
    public const string WarehouseScopeType = "Warehouse";
    public const string QueueScopeType = "Queue";
    public const string AssignmentScopeType = "Assignment";
    public const string ExplicitRecordScopeType = "ExplicitRecord";

    /// <summary>
    /// True for SuperAdmin. Means "may select any Organisation", NOT "may see every
    /// Organisation at once": even a global caller operates inside one selected
    /// <see cref="TenantId"/> at a time, which is what keeps one set of Tenant APIs working
    /// for both kinds of user.
    /// </summary>
    public bool IsGlobal => Scope == AccessScopeType.Global;

    /// <summary>
    /// True when the caller may see everything inside the Organisation they are operating in.
    ///
    /// Two cases qualify: an explicit Organisation scope, or no scope claim at all. The second
    /// is the common one — most users are never given a narrowing scope, and treating "no
    /// claim" as "see nothing" would lock out an entire Organisation on its first day.
    /// </summary>
    public bool IsTenantWide =>
        DataScopes.Count == 0 || HasScopeType(OrganisationScopeType);

    /// <summary>
    /// True when the caller carries only narrowing scopes and must be restricted to the
    /// records they own. Deliberately the inverse of the above rather than a test for one
    /// particular scope type, so an unrecognised narrowing scope fails closed, not open.
    /// </summary>
    public bool IsOwnRecordsOnly => !IsTenantWide;

    /// <summary>Campaign identifiers from Campaign scopes. Empty means no campaign restriction.</summary>
    public IReadOnlyList<Guid> CampaignIds => ParseGuids(CampaignScopeType);

    /// <summary>Record identifiers from ExplicitRecord scopes. Empty means no record restriction.</summary>
    public IReadOnlyList<Guid> ExplicitRecordIds => ParseGuids(ExplicitRecordScopeType);

    /// <summary>Geography codes from Geography scopes. Empty means no geography restriction.</summary>
    public IReadOnlyList<string> GeographyCodes => ValuesFor(GeographyScopeType);

    /// <summary>Queue codes from Queue scopes. Empty means no queue restriction.</summary>
    public IReadOnlyList<string> QueueCodes => ValuesFor(QueueScopeType);

    /// <summary>Organisation unit identifiers from Warehouse scopes.</summary>
    public IReadOnlyList<Guid> WarehouseIds => ParseGuids(WarehouseScopeType);

    public static AccessScope Empty =>
        new(Guid.Empty, null, Guid.Empty, AccessScopeType.Tenant, []);

    /// <summary>
    /// Whether this caller may touch data belonging to the given Organisation.
    ///
    /// A Tenant user matches only their own. A global caller matches whichever Organisation
    /// they have selected — NOT every Organisation — because section 48 of the brief requires
    /// the Tenant context to stay explicit even for SuperAdmin.
    /// </summary>
    public bool CanAccessTenant(Guid tenantId) => TenantId.HasValue && TenantId.Value == tenantId;

    private bool HasScopeType(string scopeType) =>
        DataScopes.Any(scope => scope.StartsWith(scopeType + ":", StringComparison.OrdinalIgnoreCase));

    /// <summary>The value half of every claim of one type. "Campaign:abc" gives "abc".</summary>
    private IReadOnlyList<string> ValuesFor(string scopeType)
    {
        var prefix = scopeType + ":";

        return
        [
            .. DataScopes
                .Where(scope => scope.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(scope => scope[prefix.Length..].Trim())
                .Where(value => value.Length > 0)
        ];
    }

    private IReadOnlyList<Guid> ParseGuids(string scopeType) =>
        [.. ValuesFor(scopeType)
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
            .Where(value => value != Guid.Empty)];
}
