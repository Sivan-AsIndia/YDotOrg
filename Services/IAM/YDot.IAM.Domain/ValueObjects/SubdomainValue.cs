using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// The Organisation subdomain label — the "ten1" in ten1.ngoplanet.com.
///
/// This is the most security-sensitive string in the tenancy model, because the host name
/// is what resolves an anonymous sign-in request to a Tenant. It is therefore validated
/// hard:
///
///   • DNS label rules: 1–63 characters, lower-case letters, digits and hyphens, and it
///     may not start or end with a hyphen.
///   • No dots. A value containing a dot could be read as a deeper host and used to point
///     one Organisation login page at another.
///   • A reserved-word list, so nobody registers "www", "api" or "admin" and takes over a
///     platform host name.
/// </summary>
public sealed partial record SubdomainValue
{
    /// <summary>
    /// Labels that belong to the platform and can never be handed to an Organisation.
    /// Registering any of these would let a Tenant intercept platform traffic.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "www", "api", "admin", "app", "mail", "smtp", "imap", "pop", "ftp", "ns", "ns1", "ns2",
        "cdn", "static", "assets", "media", "img", "images", "files", "download", "downloads",
        "portal", "login", "signin", "sign-in", "auth", "sso", "identity", "iam", "account",
        "accounts", "billing", "payment", "payments", "pay", "checkout", "support", "help",
        "docs", "documentation", "status", "health", "monitor", "metrics", "grafana", "kibana",
        "test", "testing", "dev", "development", "stage", "staging", "uat", "qa", "demo",
        "sandbox", "preview", "beta", "alpha", "internal", "private", "secure", "vpn",
        "root", "system", "superadmin", "super-admin", "businessunit", "business-unit",
        "tenant", "tenants", "organisation", "organisations", "organization", "organizations",
        "ngoplanet", "ydot", "localhost"
    };

    private SubdomainValue(string value) => Value = value;

    public string Value { get; }

    public static SubdomainValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToLowerInvariant();

        if (normalised.Length is < 1 or > 63)
        {
            return null;
        }

        // A dot here would let "evil.ten1" masquerade as a deeper host, so it is refused
        // outright rather than quietly stripped.
        if (normalised.Contains('.', StringComparison.Ordinal) || !SubdomainPattern().IsMatch(normalised))
        {
            return null;
        }

        return Reserved.Contains(normalised) ? null : new SubdomainValue(normalised);
    }

    public static SubdomainValue Parse(string candidate) =>
        TryParse(candidate) ?? throw new ArgumentException($"'{candidate}' is not a usable subdomain.", nameof(candidate));

    /// <summary>True when the label is syntactically fine but is one the platform keeps.</summary>
    public static bool IsReserved(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && Reserved.Contains(candidate.Trim().ToLowerInvariant());

    /// <summary>Builds the full host: "ten1" plus "ngoplanet.com" gives "ten1.ngoplanet.com".</summary>
    public string ToHostName(string rootDomain) =>
        $"{Value}.{rootDomain.Trim().TrimStart('.').ToLowerInvariant()}";

    public override string ToString() => Value;

    public static implicit operator string(SubdomainValue subdomain) => subdomain.Value;

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex SubdomainPattern();
}
