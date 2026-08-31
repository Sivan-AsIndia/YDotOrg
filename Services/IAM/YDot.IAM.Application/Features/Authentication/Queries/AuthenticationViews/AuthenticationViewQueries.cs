using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using Microsoft.Extensions.Options;

namespace YDot.IAM.Application.Features.Authentication.Queries.AuthenticationViews;

/// <summary>
/// Which Organisation does this host belong to? Called by the client before the sign-in form
/// is drawn.
/// </summary>
public sealed record ResolveTenantQuery(string? HostName);

/// <summary>The password rules, so the client strength meter matches the server.</summary>
public sealed record GetPasswordPolicyQuery;

/// <summary>
/// The anonymous read side of authentication.
///
/// EVERYTHING HERE IS REACHABLE WITHOUT A TOKEN, which is why each response is deliberately
/// thin. The host-resolution endpoint returns branding and lifecycle status so the sign-in
/// page can show the right organisation name and logo — and nothing whatsoever about who has
/// an account there. A "does this address exist?" endpoint would be an account-enumeration
/// oracle, so no such endpoint exists.
/// </summary>
public sealed class AuthenticationViewQueryHandler(
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ITenantContext tenantContext,
    IOptions<SecuritySettings> securityOptions,
    IOptions<TenancySettings> tenancyOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly TenancySettings _tenancy = tenancyOptions.Value;

    public async Task<Result<TenantResolutionResponse>> HandleAsync(
        ResolveTenantQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<TenantResolutionResponse>(
                Error.Dependency("The platform is not configured."));
        }

        // The middleware has usually resolved this already from the Host header. The explicit
        // host argument exists for the development case, where the Angular dev server runs on
        // localhost:6701 and has no subdomain of its own to be recognised by.
        var hostName = query.HostName ?? tenantContext.HostName;

        if (string.IsNullOrWhiteSpace(hostName))
        {
            return Result.Success(
                AuthenticationMappingConfig.ToResolutionResponse(null, businessUnit, isPlatformHost: true));
        }

        var normalised = hostName.Trim().ToLowerInvariant();

        var isPlatformHost =
            _tenancy.PlatformHosts.Contains(normalised, StringComparer.OrdinalIgnoreCase)
            || string.Equals(normalised, businessUnit.RootDomain, StringComparison.OrdinalIgnoreCase);

        if (isPlatformHost)
        {
            return Result.Success(
                AuthenticationMappingConfig.ToResolutionResponse(null, businessUnit, isPlatformHost: true));
        }

        var tenant = await tenants.ResolveByHostAsync(normalised, cancellationToken);

        // An unrecognised host is NOT an error and NOT a guess. It resolves to nothing, and
        // the client shows the platform sign-in page. Falling back to "the first Organisation"
        // would authenticate people against an Organisation they never named.
        return Result.Success(
            AuthenticationMappingConfig.ToResolutionResponse(tenant, businessUnit, isPlatformHost: false));
    }

    /// <summary>
    /// The password policy.
    ///
    /// Sent so the client strength meter agrees with what the server will accept. Without it
    /// the two drift, and people are refused for reasons the screen told them were fine.
    /// The Organisation minimum is folded in where it is stricter than the platform floor.
    /// </summary>
    public async Task<Result<PasswordPolicyResponse>> HandleAsync(
        GetPasswordPolicyQuery query, CancellationToken cancellationToken)
    {
        int? tenantMinimum = null;

        if (tenantContext.TenantId.HasValue)
        {
            var tenant = await tenants.GetByIdAsync(tenantContext.TenantId.Value, cancellationToken);
            tenantMinimum = tenant?.PasswordMinimumLength;
        }

        return Result.Success(_security.ToPolicyResponse(tenantMinimum));
    }
}
