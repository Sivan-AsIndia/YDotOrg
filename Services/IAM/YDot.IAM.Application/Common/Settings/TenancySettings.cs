namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// How a request is resolved to an Organisation.
///
/// This is the configuration behind the tenant-resolution middleware, and it is worth
/// treating as security configuration rather than convenience: the host name decides which
/// Organisation a set of credentials is checked against, so anything that loosens the
/// resolution loosens the isolation.
/// </summary>
public sealed class TenancySettings
{
    public const string SectionName = "TenancySettings";

    /// <summary>The apex domain Organisation subdomains hang off, for example ngoplanet.com.</summary>
    public string RootDomain { get; set; } = "ngoplanet.com";

    /// <summary>
    /// Hosts that mean "the platform itself" rather than any Organisation. A request arriving
    /// on one of these gets the SuperAdmin sign-in and no Tenant context.
    /// </summary>
    public string[] PlatformHosts { get; set; } = ["www.ngoplanet.com", "ngoplanet.com", "admin.ngoplanet.com"];

    /// <summary>
    /// Resolve the Organisation from the Host header. This is the production mechanism and
    /// should stay on.
    /// </summary>
    public bool ResolveFromHost { get; set; } = true;

    /// <summary>
    /// DEVELOPMENT ONLY. Lets the client name the Organisation with an X-Tenant header,
    /// because localhost:6701 has no subdomain to read.
    ///
    /// This is a genuine hole: a header is caller-controlled, so anything trusting it is
    /// trusting the caller. It is refused outright unless the request arrives on a loopback
    /// host, and it must never be enabled in a deployed environment. The token tenant_id
    /// still wins over it for any authenticated call, so at most it affects which
    /// Organisation an anonymous sign-in is checked against.
    /// </summary>
    public bool AllowHeaderOverrideOnLoopback { get; set; } = true;

    /// <summary>The header consulted when the above is enabled.</summary>
    public string TenantHeaderName { get; set; } = "X-Tenant";

    /// <summary>
    /// Organisation used for a loopback request that names none. Lets the Angular dev server
    /// sign in without a subdomain. Empty means no fallback, which is the safer setting.
    /// </summary>
    public string? DevelopmentDefaultTenantCode { get; set; }

    /// <summary>
    /// How long a resolved host to Organisation mapping is cached. The lookup happens on
    /// every anonymous request, and the mapping changes very rarely.
    /// </summary>
    public int HostResolutionCacheSeconds { get; set; } = 300;

    /// <summary>
    /// Refuses a token whose host_name claim does not match the host it arrived on. Stops a
    /// token minted for ten1 being replayed against ten2 even if the signature checks out.
    /// </summary>
    public bool EnforceTokenHostBinding { get; set; } = true;
}
