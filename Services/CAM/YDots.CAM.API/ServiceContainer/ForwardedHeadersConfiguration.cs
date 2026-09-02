using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Both namespaces define IPNetwork, and only the ASP.NET Core one is what
// ForwardedHeadersOptions.KnownNetworks holds. The alias says which, once, rather than
// fully-qualifying it at every mention below.
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace YDots.CAM.API.ServiceContainer;

/// <summary>
/// Builds the <see cref="ForwardedHeadersOptions"/> that recover the real caller address from
/// behind nginx.
///
/// WHY THIS IS A CLASS RATHER THAN AN OBJECT INITIALISER IN Program.cs, and it is not a matter
/// of taste. The options used to be written like this:
///
/// <code>
/// new ForwardedHeadersOptions
/// {
///     KnownNetworks = { },   // intended: "trust every proxy"
///     KnownProxies  = { }
/// }
/// </code>
///
/// <c>KnownNetworks = { }</c> inside an object initialiser is a COLLECTION initialiser, not an
/// assignment. It calls <c>Add</c> once per element listed, and nothing is listed — so it adds
/// nothing and LEAVES THE DEFAULTS IN PLACE. The defaults are IPv6 loopback and
/// <c>::ffff:127.0.0.0/104</c>, so the middleware still trusted loopback and only loopback.
/// nginx reaches this service from the compose bridge network (172.20.0.0/16), which is not
/// loopback, so <c>ForwardedHeadersMiddleware</c> stopped at the first unknown proxy and applied
/// nothing at all. The header was set correctly by nginx, forwarded correctly, and then ignored.
///
/// The symptom: every row this service writes recorded <c>::ffff:172.20.0.9</c> — the address of
/// the nginx CONTAINER — instead of the caller's own. So every audit row names the proxy, and
/// "where did this come from" has one answer for every event ever recorded, which is the same as
/// having no answer. IAM had the same defect, where it also flattened the per-IP sign-in rate
/// limit into a single global bucket.
///
/// Clearing the lists has to be done with <c>Clear()</c>, and that is what happens below.
///
/// TRUSTING EVERY PROXY IS STILL NOT WHAT WE WANT. X-Forwarded-For is caller-controlled, so an
/// empty known-proxy list lets anybody who can open a socket to this service write whatever
/// address they like into the audit trail and step around the per-IP limit. In this compose file
/// the container's port IS published to the host (6704), so "only nginx can reach it" is not
/// true. The trusted set is therefore explicit: loopback plus the private ranges a container
/// network uses, overridable per environment with
///
/// <code>ForwardedHeaders__KnownNetworks__0=10.42.0.0/16</code>
///
/// so a real deployment can narrow it to the one proxy in front of it.
/// </summary>
internal static class ForwardedHeadersConfiguration
{
    /// <summary>Configuration section holding the CIDR list, when one is supplied.</summary>
    private const string SectionName = "ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// Loopback plus the three RFC 1918 ranges and the IPv4-mapped forms of them.
    ///
    /// These are the addresses a reverse proxy on the same private network arrives from. They
    /// are never routable from the internet, so a forged header still cannot come from outside;
    /// what it does cover is every container network Docker, Compose and Kubernetes hand out by
    /// default, which is the whole point.
    /// </summary>
    private static readonly string[] DefaultKnownNetworks =
    [
        "127.0.0.0/8",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "::1/128"
    ];

    public static ForwardedHeadersOptions Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,

            // One hop for nginx, one spare for a load balancer in front of it. Raising this
            // without also naming the proxies would let a caller prepend addresses of their own.
            ForwardLimit = 2
        };

        // The defaults are loopback-only. Both lists have to be emptied by hand — see the class
        // comment for why the obvious-looking initialiser did not do it.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        var configured = configuration.GetSection(SectionName).Get<string[]>();

        var networks = configured is { Length: > 0 } ? configured : DefaultKnownNetworks;

        foreach (var entry in networks)
        {
            if (TryParseNetwork(entry, out var network))
            {
                options.KnownNetworks.Add(network);
            }
        }

        return options;
    }

    /// <summary>
    /// Parses one CIDR entry.
    ///
    /// A malformed entry is skipped rather than thrown on. A typo in an environment variable
    /// should narrow what is trusted — the safe direction — not stop the service booting.
    /// </summary>
    private static bool TryParseNetwork(string entry, [NotNullWhen(true)] out IPNetwork? network)
    {
        network = null;

        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var parts = entry.Split('/', 2, StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var maximumLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? 128
            : 32;

        if (prefixLength < 0 || prefixLength > maximumLength)
        {
            return false;
        }

        network = new IPNetwork(prefix, prefixLength);
        return true;
    }
}
