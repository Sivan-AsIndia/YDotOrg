using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YDots.CAM.Application.Common.Constants;

namespace YDots.CAM.Infrastructure.Multitenancy;

/// <summary>
/// Fills in the request Organisation from the validated token.
///
/// WHERE IT SITS IN THE PIPELINE IS LOAD-BEARING. It must run AFTER
/// <c>UseAuthentication</c>, because it reads claims that only exist once the token has been
/// validated, and BEFORE the endpoint, because the query filters read what it sets. Registered
/// in the wrong order it silently resolves nothing, every filter matches nothing, and every
/// list comes back empty with no error anywhere.
///
/// IT READS ONLY THE TOKEN. No header, no query string, no request body - nothing the caller
/// can set without our signature on it. That is the whole reason a caller cannot choose which
/// Organisation they operate in.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            // tenant_id first, organisation_id as the fallback. IAM writes the same value into
            // both - the second for the services that predate the tenancy vocabulary - so
            // reading either works, and reading both means CAM keeps working whichever IAM
            // version is deployed.
            var tenantId = ParseGuid(
                Find(principal, ClaimTypeNames.TenantId) ?? Find(principal, ClaimTypeNames.OrganisationId));

            var businessUnitId = ParseGuid(Find(principal, ClaimTypeNames.BusinessUnitId)) ?? Guid.Empty;

            var isSuperAdmin = string.Equals(
                Find(principal, ClaimTypeNames.IsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

            tenantContext.Set(
                tenantId,
                businessUnitId,
                Find(principal, ClaimTypeNames.TenantCode),
                Find(principal, ClaimTypeNames.TenantName),
                isSuperAdmin);

            // A SuperAdmin who has not selected an Organisation is the ordinary case for
            // platform work, so this is not an error - but a TENANT user with no tenant_id is a
            // token IAM should never have issued, and it is worth knowing about.
            if (tenantId is null && !isSuperAdmin)
            {
                logger.LogWarning(
                    "An authenticated non-super-admin request carried no organisation claim. "
                    + "Every organisation-scoped read will return empty. Correlation {CorrelationId}.",
                    context.TraceIdentifier);
            }
        }

        await next(context);
    }

    private static string? Find(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}
