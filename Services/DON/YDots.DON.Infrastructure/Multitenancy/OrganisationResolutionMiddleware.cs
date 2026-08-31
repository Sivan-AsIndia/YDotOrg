using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YDots.DON.Application.Common.Constants;

namespace YDots.DON.Infrastructure.Multitenancy;

/// <summary>
/// Fills in the request Organisation from the validated token.
///
/// WHERE IT SITS IN THE PIPELINE IS LOAD-BEARING. It must run AFTER <c>UseAuthentication</c>,
/// because it reads claims that only exist once the token has been validated, and BEFORE the
/// endpoint, because the query filters read what it sets. Registered in the wrong order it
/// silently resolves nothing, every filter matches nothing, and every list comes back empty
/// with no error anywhere.
///
/// IT READS ONLY THE TOKEN. No header, no query string, no request body - nothing the caller
/// can set without IAM's signature on it. That is the whole reason a caller cannot choose which
/// Organisation they operate in.
/// </summary>
public sealed class OrganisationResolutionMiddleware(
    RequestDelegate next,
    ILogger<OrganisationResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            // organisation_id first, because that is the claim DON has always read. tenant_id
            // is the newer name IAM writes the same value into, so reading both means DON keeps
            // working whichever IAM version is deployed.
            var raw = principal.FindFirst(ClaimTypeNames.OrganisationId)?.Value
                      ?? principal.FindFirst("tenant_id")?.Value;

            var organisationId = Guid.TryParse(raw, out var parsed) ? parsed : (Guid?)null;

            tenantContext.Set(organisationId);

            if (organisationId is null)
            {
                // A SuperAdmin doing platform work legitimately has no Organisation. It is worth
                // a warning anyway, because every organisation-scoped read is about to return
                // empty and that is otherwise indistinguishable from "there is no data".
                logger.LogWarning(
                    "An authenticated request carried no organisation claim. Every "
                    + "organisation-scoped read will return empty. Correlation {CorrelationId}.",
                    context.TraceIdentifier);
            }
        }

        await next(context);
    }
}
