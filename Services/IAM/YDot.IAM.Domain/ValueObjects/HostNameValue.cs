using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// A full, normalised host name as it arrives on the request — "ten1.ngoplanet.com".
///
/// The tenant-resolution middleware turns the raw <c>Host</c> header into one of these
/// before it looks anything up, and that normalisation is a security control rather than
/// tidiness. A <c>Host</c> header is attacker-controlled, so it is cleaned up in exactly
/// one place:
///
///   • the port is stripped, so "ten1.ngoplanet.com:4200" and "ten1.ngoplanet.com" resolve
///     to the same Organisation instead of one of them silently failing to resolve;
///   • a trailing dot (the DNS root, legal in a Host header) is removed, so
///     "ten1.ngoplanet.com." cannot be used as a second spelling of the same host;
///   • the whole thing is lower-cased, because DNS is case-insensitive but a string index
///     is not.
///
/// Anything that is not a plausible host — an empty value, an IPv6 literal in brackets,
/// something with a slash in it — comes back null, and an unresolved host gets the
/// platform sign-in page rather than a guess.
/// </summary>
public sealed partial record HostNameValue
{
    private HostNameValue(string value) => Value = value;

    public string Value { get; }

    /// <summary>The left-most label: "ten1" from "ten1.ngoplanet.com".</summary>
    public string FirstLabel
    {
        get
        {
            var index = Value.IndexOf('.', StringComparison.Ordinal);
            return index < 0 ? Value : Value[..index];
        }
    }

    /// <summary>Everything after the first label: "ngoplanet.com" from "ten1.ngoplanet.com".</summary>
    public string? Parent
    {
        get
        {
            var index = Value.IndexOf('.', StringComparison.Ordinal);
            return index < 0 ? null : Value[(index + 1)..];
        }
    }

    /// <summary>True for the loopback names used in development.</summary>
    public bool IsLoopback =>
        Value is "localhost" or "127.0.0.1" or "::1" || Value.EndsWith(".localhost", StringComparison.Ordinal);

    public static HostNameValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToLowerInvariant();

        // Strip a scheme if somebody passed a whole URL.
        var schemeIndex = normalised.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            normalised = normalised[(schemeIndex + 3)..];
        }

        // Strip anything from the first slash onward.
        var slashIndex = normalised.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            normalised = normalised[..slashIndex];
        }

        // Strip the port. Guarded so an IPv6 literal is rejected below rather than mangled.
        var colonIndex = normalised.LastIndexOf(':');
        if (colonIndex > 0 && !normalised.Contains(']', StringComparison.Ordinal))
        {
            normalised = normalised[..colonIndex];
        }

        // A trailing dot is the DNS root and is legal on the wire, but it must not become a
        // second spelling of the same host.
        normalised = normalised.TrimEnd('.');

        if (normalised.Length is 0 or > 253)
        {
            return null;
        }

        return normalised is "localhost" or "127.0.0.1" || HostPattern().IsMatch(normalised)
            ? new HostNameValue(normalised)
            : null;
    }

    public static HostNameValue Parse(string candidate) =>
        TryParse(candidate) ?? throw new ArgumentException($"'{candidate}' is not a valid host name.", nameof(candidate));

    public override string ToString() => Value;

    public static implicit operator string(HostNameValue host) => host.Value;

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex HostPattern();
}
