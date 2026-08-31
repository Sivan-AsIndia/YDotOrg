using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// One host name that reaches one Organisation.
///
/// WHY THIS IS A SEPARATE TABLE. <c>Tenant.Subdomain</c> alone would be enough for
/// ten1.ngoplanet.com, but section 33 of the brief asks that a custom domain such as
/// www.someorganisation.org be supportable later "without redesigning the Tenant entity".
/// Modelling the host as its own row does that: an Organisation can hold several hosts,
/// one of them primary, each verified independently.
///
/// THIS TABLE IS THE ANONYMOUS ENTRY POINT. Before anybody has a token, the only thing the
/// server knows about a sign-in request is the host it arrived on. That host is looked up
/// here, and the row decides which Tenant the credentials are checked against. It is
/// therefore the one lookup that must be exact — no prefix matching, no fallback to "the
/// first Tenant" — which is why <see cref="HostName"/> carries a unique index and is
/// stored fully normalised.
/// </summary>
public class TenantDomain : AuditEntity, IBusinessUnitOwned
{
    public Guid BusinessUnitId { get; set; }

    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>
    /// The complete, lower-cased host with no scheme, no port and no trailing dot:
    /// "ten1.ngoplanet.com". Unique platform-wide — a host can only point at one
    /// Organisation.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    public TenantDomainType DomainType { get; set; } = TenantDomainType.Subdomain;

    /// <summary>
    /// The host used when the platform has to build a link back to this Organisation —
    /// an invitation e-mail, a password reset. Exactly one row per Tenant carries this.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Whether ownership of the host has been proven. A platform subdomain is verified on
    /// creation because the platform already controls the apex; a custom domain is not
    /// verified until the DNS record appears.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>The random token the Organisation publishes as a DNS TXT record to prove ownership.</summary>
    public string? VerificationToken { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public Guid? VerifiedByUserId { get; set; }

    /// <summary>
    /// A host can be taken out of service without being deleted, so the row and its history
    /// survive and the host cannot immediately be claimed by a different Organisation.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True when this host may actually be used to resolve a Tenant right now.</summary>
    public bool IsUsable => IsActive && IsVerified;
}
