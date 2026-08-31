namespace YDot.IAM.Domain.Enums;

/// <summary>
/// How a host name reaches a Tenant. Keeping this on a separate <c>TenantDomain</c> row
/// rather than a single column on Tenant is what lets a custom domain be added later
/// without redesigning the Tenant entity (section 33 of the brief).
/// </summary>
public enum TenantDomainType
{
    /// <summary>ten1.ngoplanet.com — a subdomain of the BusinessUnit root domain.</summary>
    Subdomain = 0,

    /// <summary>www.someorganisation.org — a domain the Organisation owns outright.</summary>
    CustomDomain = 1,

    /// <summary>An alias that redirects to the primary host.</summary>
    Alias = 2
}
