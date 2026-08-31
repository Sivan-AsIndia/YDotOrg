using System.Net;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Infrastructure.Multitenancy;

namespace YDot.IAM.Api.Middleware;

/// <summary>
/// Resolves the Organisation for every request, and fills in <c>ITenantContext</c>.
///
/// WHERE THIS SITS IN THE PIPELINE IS THE WHOLE DESIGN. It must run AFTER
/// <c>UseAuthentication</c>, because the token is the strongest statement about which
/// Organisation the caller belongs to and the middleware needs the validated principal to read
/// it. It must run BEFORE <c>UseAuthorization</c> and before any endpoint, because the
/// authorization policies and every query filter downstream depend on the answer.
///
/// <code>
/// UseAuthentication   validates the token
///        v
/// TenantResolution    THIS - decides which Organisation the request operates in
///        v
/// UseAuthorization    policies that check tenant context
///        v
/// endpoint            handlers, query filters, write stamping
/// </code>
///
/// IT NEVER READS A BODY OR A QUERY STRING. Section 47 of the brief: the Organisation is not
/// something a caller may assert. Only a signed token, the host, or — on loopback only — a
/// development header.
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancySettings> tenancyOptions,
    ILogger<TenantResolutionMiddleware> logger)
{
    private readonly TenancySettings _tenancy = tenancyOptions.Value;

    public async Task InvokeAsync(
        HttpContext context, TenantContext tenantContext, TenantResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(context);

        var host = context.Request.Host.Host;

        // The development header is only ever consulted from loopback. Anywhere else, a header
        // is caller-controlled and trusting it would hand the caller the isolation boundary.
        var isLoopback = IsLoopback(context);

        var tenantHeader = isLoopback && _tenancy.AllowHeaderOverrideOnLoopback
            ? context.Request.Headers[_tenancy.TenantHeaderName].ToString()
            : null;

        var resolution = await resolver.ResolveAsync(
            context.User, host, tenantHeader, isLoopback, context.RequestAborted);

        // Scope and root-user status come from the VALIDATED token, never from the host.
        var isSuperAdmin = string.Equals(
            context.User.FindFirst(Application.Common.Constants.ClaimTypeNames.IsSuperAdmin)?.Value,
            "true", StringComparison.OrdinalIgnoreCase);

        var scope = Enum.TryParse<Domain.Enums.AccessScopeType>(
            context.User.FindFirst(Application.Common.Constants.ClaimTypeNames.Scope)?.Value,
            out var parsedScope)
            ? parsedScope
            : Domain.Enums.AccessScopeType.Tenant;

        tenantContext.Set(
            resolution.TenantId,
            resolution.BusinessUnitId,
            resolution.TenantCode,
            resolution.TenantName,
            resolution.Status,
            scope,
            isSuperAdmin,
            resolution.HostName,
            resolution.IsPlatformHost);

        // ---- Token host binding -------------------------------------------------------
        //
        // A token carries the host it was issued for. Refusing a mismatch stops one minted at
        // ten1.ngoplanet.com being replayed against ten2 even though the signature is perfectly
        // valid. Loopback is exempt, because the dev server has no subdomain to match.
        if (_tenancy.EnforceTokenHostBinding
            && context.User.Identity?.IsAuthenticated == true
            && !isLoopback)
        {
            var tokenHost = context.User
                .FindFirst(Application.Common.Constants.ClaimTypeNames.HostName)?.Value;

            if (!string.IsNullOrWhiteSpace(tokenHost)
                && !string.Equals(tokenHost, resolution.HostName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "A token issued for {TokenHost} was presented at {RequestHost}. Refused.",
                    tokenHost, resolution.HostName);

                await WriteForbiddenAsync(context);
                return;
            }
        }

        // Useful on every log line for the rest of the request.
        context.Items["TenantId"] = resolution.TenantId;
        context.Items["TenantCode"] = resolution.TenantCode;

        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["TenantId"] = resolution.TenantId,
                   ["TenantCode"] = resolution.TenantCode,
                   ["HostName"] = resolution.HostName
               }))
        {
            await next(context);
        }
    }

    /// <summary>
    /// True when the request came from the machine itself.
    ///
    /// Both the remote and the local address are checked: a request through the Kestrel
    /// loopback listener has both, and requiring both means a forwarded request from outside
    /// cannot pass by spoofing one of them.
    /// </summary>
    private static bool IsLoopback(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;

        if (remote is null)
        {
            // No remote address at all means an in-process call, which is loopback by
            // definition - the test host does this.
            return true;
        }

        return IPAddress.IsLoopback(remote)
               || (context.Connection.LocalIpAddress is not null
                   && remote.Equals(context.Connection.LocalIpAddress));
    }

    /// <summary>
    /// The same envelope as every other failure, so the Angular interceptor needs no second
    /// code path for a refusal that happens this early in the pipeline.
    /// </summary>
    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var error = Application.Common.Results.Error.CrossTenantAccessDenied(
            "This session belongs to a different organisation. Sign in again at this address.");

        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        context.Response.StatusCode = error.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(
            Application.Common.Results.ApiResponse.Fail(error, correlationId));
    }
}
