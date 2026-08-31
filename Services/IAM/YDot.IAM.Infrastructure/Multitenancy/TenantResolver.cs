using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Multitenancy;

/// <summary>
/// Works out which Organisation a request belongs to.
///
/// THE PRIORITY ORDER IS THE SECURITY DESIGN:
///
/// <code>
/// 1. tenant_id claim of a VALIDATED JWT     signed by us, so it cannot be edited. Always wins.
/// 2. the Host header                        for anonymous requests, which have no token yet.
/// 3. X-Tenant header, LOOPBACK ONLY         so the Angular dev server can work without subdomains.
/// </code>
///
/// The token wins over the host on purpose. A signed token is the strongest statement we have
/// about who the caller is; the host is merely where the request arrived. If the two disagree
/// — a token for TEN001 presented at ten2.ngoplanet.com — the token decides which Organisation
/// the caller operates in, and <see cref="TenancySettings.EnforceTokenHostBinding"/> can
/// refuse the mismatch outright rather than quietly allowing it.
///
/// NOTHING HERE READS A REQUEST BODY, A QUERY STRING, OR ANYTHING THE BROWSER STORED. That is
/// section 47 of the brief, and it is the whole reason this class exists rather than each
/// handler working the Organisation out for itself.
/// </summary>
public sealed class TenantResolver(
    IDbContextFactory<IamDbContext> contextFactory,
    IMemoryCache cache,
    IOptions<TenancySettings> tenancyOptions,
    ILogger<TenantResolver> logger)
{
    private readonly TenancySettings _tenancy = tenancyOptions.Value;

    /// <summary>What a host or a token resolved to.</summary>
    public sealed record Resolution(
        Guid? TenantId,
        Guid BusinessUnitId,
        string? TenantCode,
        string? TenantName,
        TenantStatus? Status,
        bool IsPlatformHost,
        string? HostName);

    /// <summary>
    /// Resolves the Organisation for a request.
    ///
    /// <paramref name="principal"/> is the already-validated token principal, or null for an
    /// anonymous request. It is trusted here precisely because the authentication middleware
    /// has already checked its signature, issuer, audience and lifetime — an unvalidated
    /// token must never reach this method.
    /// </summary>
    public async Task<Resolution> ResolveAsync(
        ClaimsPrincipal? principal,
        string? rawHost,
        string? tenantHeader,
        bool isLoopback,
        CancellationToken cancellationToken)
    {
        var host = HostNameValue.TryParse(rawHost);
        var hostName = host?.Value;

        // ---- 1. The token, when there is one -------------------------------------------
        if (principal?.Identity?.IsAuthenticated == true)
        {
            var fromToken = await ResolveFromTokenAsync(principal, hostName, cancellationToken);
            if (fromToken is not null)
            {
                return fromToken;
            }
        }

        // ---- 2. The development header, loopback only -----------------------------------------
        //
        // CHECKED BEFORE THE HOST, and only because of what the host would otherwise do to
        // it: on a developer machine the host IS a platform host (localhost), so letting the
        // host win first would resolve every headered request to the platform and the header
        // would never be read. Ordering it here is what keeps `X-Tenant: ten1` meaning what it
        // says while a browser on ten1.localhost still resolves by name.
        //
        // A header is caller-controlled, so trusting one is trusting the caller. It is
        // therefore gated on THREE conditions at once: the setting is on, the request came
        // from loopback, and there is no token (a token would already have won above). In a
        // deployed environment the loopback test alone makes this unreachable.
        if (_tenancy.AllowHeaderOverrideOnLoopback && isLoopback && !string.IsNullOrWhiteSpace(tenantHeader))
        {
            var resolved = await ResolveByCodeOrHostAsync(tenantHeader.Trim(), cancellationToken);
            if (resolved is not null)
            {
                logger.LogWarning(
                    "Organisation {TenantCode} was resolved from the {Header} header on a loopback request. "
                    + "This path must never be enabled outside development.",
                    resolved.TenantCode, _tenancy.TenantHeaderName);

                return resolved;
            }
        }

        // ---- 3. The host ------------------------------------------------------------------
        var businessUnit = await GetDefaultBusinessUnitAsync(cancellationToken);

        // A LOOPBACK NAME IS RESOLVED LIKE ANY OTHER. This block used to skip `localhost` and
        // anything under `.localhost` entirely, which meant a deployment that deliberately
        // configured `localhost` as its platform host - a container published on
        // localhost:6700, say - had that configuration silently ignored, and `ten1.localhost`
        // never resolved to an Organisation either.
        //
        // Nothing is weakened by looking. Both tests below read SERVER configuration, never
        // anything the caller sends: an unlisted host that matches no subdomain still falls
        // through to the same "no Organisation" answer it reached before. The caller-controlled
        // path is the X-Tenant header further down, and that gate is untouched.
        if (host is not null)
        {
            var isPlatform =
                _tenancy.PlatformHosts.Contains(host.Value, StringComparer.OrdinalIgnoreCase)
                || (businessUnit is not null
                    && string.Equals(host.Value, businessUnit.RootDomain, StringComparison.OrdinalIgnoreCase));

            if (isPlatform)
            {
                return new Resolution(
                    null, businessUnit?.Id ?? Guid.Empty, null, null, null,
                    IsPlatformHost: true, hostName);
            }

            if (_tenancy.ResolveFromHost)
            {
                var resolved = await ResolveFromHostAsync(host.Value, cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        // A configured development default, so the dev server can sign in with no subdomain.
        if (isLoopback && !string.IsNullOrWhiteSpace(_tenancy.DevelopmentDefaultTenantCode))
        {
            var resolved = await ResolveByCodeOrHostAsync(_tenancy.DevelopmentDefaultTenantCode, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        // ---- Nothing matched --------------------------------------------------------------------
        //
        // Loopback with nothing configured is treated as the platform host, so a developer
        // gets the SuperAdmin sign-in rather than a dead end. A real unrecognised host also
        // resolves to no Organisation, and the sign-in handler refuses it — which is the
        // correct failure: guessing an Organisation would authenticate people against one
        // they never named.
        return new Resolution(
            null, businessUnit?.Id ?? Guid.Empty, null, null, null,
            IsPlatformHost: isLoopback || host is null, hostName);
    }

    /// <summary>
    /// Reads the Organisation out of a validated token.
    ///
    /// The claim is trusted because we signed it. What is NOT trusted is that the Organisation
    /// still exists and is in a usable state, so the row is loaded and its current status
    /// returned — a token minted before an Organisation was suspended must not keep working.
    /// </summary>
    private async Task<Resolution?> ResolveFromTokenAsync(
        ClaimsPrincipal principal, string? hostName, CancellationToken cancellationToken)
    {
        var businessUnitId = ParseGuid(principal.FindFirst(ClaimTypeNames.BusinessUnitId)?.Value);

        var tenantIdValue = principal.FindFirst(ClaimTypeNames.TenantId)?.Value
                            // Falls back to the DON-compatible claim, which carries the same value.
                            ?? principal.FindFirst(ClaimTypeNames.OrganisationId)?.Value;

        var tenantId = ParseGuid(tenantIdValue);

        // A Global-scope token with no tenant_id is SuperAdmin before they have selected an
        // Organisation. Perfectly valid, and deliberately carries no Organisation.
        if (tenantId is null)
        {
            var businessUnit = await GetDefaultBusinessUnitAsync(cancellationToken);

            return new Resolution(
                null,
                businessUnitId ?? businessUnit?.Id ?? Guid.Empty,
                null, null, null,
                IsPlatformHost: true,
                hostName);
        }

        var tenant = await GetTenantAsync(tenantId.Value, cancellationToken);
        if (tenant is null)
        {
            // The token names an Organisation that no longer exists. Nothing is resolved, so
            // every Tenant-owned read returns empty rather than falling back to something.
            logger.LogWarning(
                "A token named organisation {TenantId}, which no longer exists.", tenantId);

            return null;
        }

        return new Resolution(
            tenant.Id, tenant.BusinessUnitId, tenant.Code, tenant.Name, tenant.Status,
            IsPlatformHost: false, hostName);
    }

    /// <summary>
    /// Host to Organisation, through <c>TenantDomain</c>.
    ///
    /// Cached, because this runs on every anonymous request and the mapping changes very
    /// rarely. The cache is keyed on the exact normalised host, so there is no prefix or
    /// fuzzy matching to get wrong.
    /// </summary>
    private async Task<Resolution?> ResolveFromHostAsync(string hostName, CancellationToken cancellationToken)
    {
        var cacheKey = $"tenant:host:{hostName}";

        if (cache.TryGetValue<Resolution>(cacheKey, out var cached) && cached is not null)
        {
            // The STATUS is deliberately re-read rather than served from cache: an
            // Organisation suspended a minute ago must stop working now, not in five minutes.
            var current = await GetTenantAsync(cached.TenantId!.Value, cancellationToken);

            return current is null
                ? null
                : cached with { Status = current.Status, TenantName = current.Name };
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var match = await context.TenantDomains
            .AsNoTracking()
            .Where(domain => domain.HostName == hostName && domain.IsActive && domain.IsVerified)
            .Select(domain => new
            {
                domain.TenantId,
                domain.BusinessUnitId,
                TenantCode = domain.Tenant!.Code,
                TenantName = domain.Tenant.Name,
                domain.Tenant.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return null;
        }

        var resolution = new Resolution(
            match.TenantId, match.BusinessUnitId, match.TenantCode, match.TenantName,
            match.Status, IsPlatformHost: false, hostName);

        cache.Set(cacheKey, resolution, TimeSpan.FromSeconds(_tenancy.HostResolutionCacheSeconds));

        return resolution;
    }

    /// <summary>Development helper: accepts an Organisation code, a subdomain or a full host.</summary>
    private async Task<Resolution?> ResolveByCodeOrHostAsync(string value, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var normalised = value.Trim().ToUpperInvariant();
        var lower = value.Trim().ToLowerInvariant();

        var tenant = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Code == normalised || item.Subdomain == lower,
                cancellationToken);

        if (tenant is null)
        {
            var byHost = await context.TenantDomains
                .AsNoTracking()
                .Where(domain => domain.HostName == lower)
                .Select(domain => domain.Tenant)
                .FirstOrDefaultAsync(cancellationToken);

            tenant = byHost;
        }

        return tenant is null
            ? null
            : new Resolution(
                tenant.Id, tenant.BusinessUnitId, tenant.Code, tenant.Name, tenant.Status,
                IsPlatformHost: false, HostName: null);
    }

    private async Task<Domain.Entities.Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
    }

    /// <summary>
    /// The BusinessUnit the platform runs as. Cached for longer than the host mapping, because
    /// it effectively never changes.
    /// </summary>
    private async Task<Domain.Entities.BusinessUnit?> GetDefaultBusinessUnitAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "platform:business-unit";

        if (cache.TryGetValue<Domain.Entities.BusinessUnit>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var businessUnit = await context.BusinessUnits
            .AsNoTracking()
            .OrderBy(unit => unit.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (businessUnit is not null)
        {
            cache.Set(cacheKey, businessUnit, TimeSpan.FromMinutes(10));
        }

        return businessUnit;
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}
