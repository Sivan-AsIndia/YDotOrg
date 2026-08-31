namespace YDots.DON.Application.Common.Models;

/// <summary>
/// The effective data scope of the caller, passed to <c>IDonorReadService</c> exactly as the
/// application interface table requires.
///
/// UI section 2: "Every count, search, suggestion, list, card, detail, action, export and
/// notification is restricted by effective scope." This record is what carries that restriction
/// into the query, so no repository has to reach for HttpContext.
///
/// THE CLAIM FORMAT IS AN IAM CONTRACT. IAM's SessionTokenService writes one data_scope claim
/// per assignment as "{ScopeType}:{ScopeValue}", where ScopeType is a member of its
/// DataScopeType enum: Organisation, Geography, Campaign, Warehouse, Queue, Assignment or
/// ExplicitRecord. The parsing below has to keep matching that shape.
/// </summary>
public sealed record AccessScope(
    Guid OrganisationId,
    Guid UserId,
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
    /// True when the caller may see everything inside their organisation.
    ///
    /// Two cases qualify: an explicit Organisation scope, or no scope claim at all. The second
    /// is the common one — most users are never given a narrowing scope, and treating "no
    /// claim" as "see nothing" would lock out the whole fundraising team on day one.
    /// </summary>
    public bool IsOrganisationWide =>
        DataScopes.Count == 0 || HasScopeType(OrganisationScopeType);

    /// <summary>
    /// True when the caller carries only narrowing scopes and must therefore be restricted to
    /// the records they own. Deliberately the inverse of the above rather than a test for one
    /// particular scope type: an unrecognised narrowing scope fails closed, not open.
    /// </summary>
    public bool IsOwnRecordsOnly => !IsOrganisationWide;

    /// <summary>Campaign identifiers from Campaign scopes. Empty means "no campaign restriction".</summary>
    public IReadOnlyList<Guid> CampaignIds => ParseGuids(CampaignScopeType);

    /// <summary>Record identifiers from ExplicitRecord scopes. Empty means "no record restriction".</summary>
    public IReadOnlyList<Guid> ExplicitRecordIds => ParseGuids(ExplicitRecordScopeType);

    /// <summary>Geography codes from Geography scopes. Empty means "no geography restriction".</summary>
    public IReadOnlyList<string> GeographyCodes => ValuesFor(GeographyScopeType);

    /// <summary>Queue codes from Queue scopes. Empty means "no queue restriction".</summary>
    public IReadOnlyList<string> QueueCodes => ValuesFor(QueueScopeType);

    public static AccessScope Empty => new(Guid.Empty, Guid.Empty, []);

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
        [.. ValuesFor(scopeType).Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
            .Where(value => value != Guid.Empty)];
}
