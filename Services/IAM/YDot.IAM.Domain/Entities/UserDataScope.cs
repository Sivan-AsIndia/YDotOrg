using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One narrowing data scope granted to one user INSIDE their Organisation.
///
/// These become the <c>data_scope</c> claims in the access token, written as
/// "{ScopeType}:{ScopeValue}" — the exact shape the Donors service parses. Adding a scope
/// type here without adding it there means DON will ignore the claim, so the two must move
/// together.
///
/// SCOPES NARROW, THEY NEVER WIDEN. A user with no scope row sees their whole Organisation;
/// a user with a Campaign scope sees only those campaigns. There is no scope value that
/// reaches outside the Tenant, and there could not be: the query filter and the token
/// tenant_id both sit above this.
/// </summary>
public class UserDataScope : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public DataScopeType ScopeType { get; set; }

    /// <summary>
    /// The identifier or code being scoped to: a campaign Guid, a geography code, a queue
    /// code. Held as text because the meaning varies by type.
    /// </summary>
    public string ScopeValue { get; set; } = string.Empty;

    /// <summary>Human-readable label so the access-preview screen need not resolve every id.</summary>
    public string? DisplayLabel { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; }

    public Guid GrantedByUserId { get; set; }

    public DateTimeOffset EffectiveFromUtc { get; set; }

    /// <summary>Null means permanent.</summary>
    public DateTimeOffset? EffectiveToUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>Set when the scope came from an approved access request.</summary>
    public Guid? SourceAccessRequestId { get; set; }

    public bool IsEffective(DateTimeOffset asOf) =>
        RevokedAtUtc is null
        && EffectiveFromUtc <= asOf
        && (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > asOf);

    /// <summary>The claim value the token carries: "Campaign:9fb1...".</summary>
    public string ToClaimValue() => $"{ScopeType}:{ScopeValue}";
}
