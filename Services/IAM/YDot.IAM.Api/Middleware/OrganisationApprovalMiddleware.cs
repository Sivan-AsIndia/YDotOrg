using Microsoft.AspNetCore.Authorization;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Middleware;

/// <summary>
/// Refuses Tenant work until the Organisation has been approved.
///
/// IF YOU HAVE NOT MET MIDDLEWARE BEFORE: every HTTP request that arrives walks through a
/// queue of small components before it reaches the controller that answers it. Each one may
/// look at the request, change it, let it carry on - or stop it dead and answer by itself.
/// That queue is set up in <c>Program.cs</c>, and the ORDER is the design, because each step
/// can only use what the steps above it have already worked out. This class is one of those
/// steps, and its job is a single yes-or-no: has this Organisation been approved yet?
///
/// WHY THIS EXISTS AT ALL. The sidebar already stops showing the other modules while an
/// Organisation is onboarding — see filter 5 in <c>MenuBuilderService</c> — but the navigation
/// tree is explicitly NOT a security boundary, and here that distinction has teeth. The
/// TenantAdmin role is created with <c>GrantsAllTenantPermissions</c> at the same moment the
/// Organisation is, so every permission check downstream passes. Without this middleware a
/// hand-typed URL reaches a working screen and an Organisation nobody has agreed to do
/// business with can create campaigns, add donors and invite staff.
///
/// DENY BY DEFAULT, with a named list of exceptions. A controller added later is refused
/// during onboarding until somebody marks it <c>[AllowedWhileOnboarding]</c>, which is the
/// safe direction to fail — the alternative is a new module that quietly works before
/// approval because nobody remembered this file existed.
///
/// WHERE IT SITS. After <c>TenantResolutionMiddleware</c>, because it needs the resolved
/// Organisation's status; before <c>UseAuthorization</c>, so the refusal is the same for every
/// endpoint rather than depending on which policy that endpoint happened to use. Routing has
/// already run by this point, so the endpoint's metadata is available to read.
///
/// <code>
/// UseAuthentication      validates the token
///        v
/// TenantResolution       decides which Organisation the request operates in
///        v
/// OrganisationApproval   THIS — refuses everything but onboarding work until approved
///        v
/// UseAuthorization       policies and permissions
/// </code>
///
/// THREE CALLERS PASS THROUGH UNTOUCHED:
///
/// <list type="bullet">
/// <item>Anonymous endpoints. Sign-in and invitation acceptance have to work, or the
/// administrator of an unapproved Organisation could never get in to finish it.</item>
/// <item>SuperAdmin. Entering an Organisation that is still onboarding is precisely how a
/// submission gets reviewed, so gating them would break approval itself.</item>
/// <item>Requests with no Organisation resolved. There is no status to judge, and the
/// endpoint's own policies still apply.</item>
/// </list>
///
/// SUSPENDED AND ARCHIVED ARE NOT THIS MIDDLEWARE'S PROBLEM. They are decisions about an
/// Organisation that was already live, they refuse sign-in outright, and the refresh path
/// revokes the token chain within one access-token lifetime. This gate is only about the
/// statuses on the way IN.
/// </summary>
public sealed class OrganisationApprovalMiddleware(
    RequestDelegate next,
    ILogger<OrganisationApprovalMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(currentUser);

        if (IsPermitted(context, tenantContext, currentUser))
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "{Path} was refused: organisation {TenantCode} is {Status} and not yet approved.",
            context.Request.Path, tenantContext.TenantCode, tenantContext.TenantStatus);

        await WriteNotApprovedAsync(context);
    }

    private static bool IsPermitted(
        HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        // No Organisation resolved means there is no status to judge. The platform host and
        // the sign-in screen both land here.
        if (!tenantContext.TenantStatus.HasValue
            || !Tenant.IsOnboardingStatus(tenantContext.TenantStatus.Value))
        {
            return true;
        }

        // Reviewing a submission means being inside an Organisation that is, by definition,
        // not approved yet.
        if (currentUser.IsSuperAdmin)
        {
            return true;
        }

        var endpoint = context.GetEndpoint();

        // No endpoint means routing matched nothing; let the 404 happen rather than dressing
        // it up as a permission failure.
        if (endpoint is null)
        {
            return true;
        }

        return endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
               || endpoint.Metadata.GetMetadata<AllowedWhileOnboardingAttribute>() is not null;
    }

    /// <summary>
    /// The same envelope every other failure uses, so the Angular interceptor already knows
    /// what to do with it — TENANT_NOT_APPROVED is the code the sign-in path returns for an
    /// ordinary user of an unapproved Organisation.
    /// </summary>
    private static async Task WriteNotApprovedAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var error = Application.Common.Results.Error.TenantNotApproved(
            "This organisation is still awaiting approval. "
            + "Finish the organisation profile and submit it for review.");

        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        context.Response.StatusCode = error.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(
            Application.Common.Results.ApiResponse.Fail(error, correlationId));
    }
}
