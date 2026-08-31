using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Authorization;

/// <summary>One permission code the endpoint asks for.</summary>
public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>The caller must be the platform root user.</summary>
public sealed class SuperAdminRequirement : IAuthorizationRequirement;

/// <summary>The caller must administer the Organisation they are operating in.</summary>
public sealed class TenantAdminRequirement : IAuthorizationRequirement;

/// <summary>An Organisation must be resolved for this request.</summary>
public sealed class TenantContextRequirement : IAuthorizationRequirement;

/// <summary>The caller must have re-authenticated recently.</summary>
public sealed class RecentReauthenticationRequirement : IAuthorizationRequirement;

/// <summary>The token must be a full access token, not a half-authenticated one.</summary>
public sealed class FullAccessTokenRequirement : IAuthorizationRequirement;

/// <summary>The caller may not act on a record whose subject is themselves.</summary>
public sealed class IndependentActorRequirement(string routeValueName) : IAuthorizationRequirement
{
    public string RouteValueName { get; } = routeValueName;
}

/// <summary>
/// Permission-based authorization.
///
/// The check is a CLAIM LOOKUP with no database call, because the token already carries one
/// claim per permission code. That is what lets an endpoint be authorised in microseconds and
/// what lets sibling services enforce IAM permissions without ever calling IAM.
///
/// SUPERADMIN SHORT-CIRCUITS, for the reason set out on <c>ICurrentUser.HasPermission</c>:
/// their token deliberately carries no permission claims, and section 4.1 of the brief
/// requires them to reach every Tenant module without being individually assigned each one.
/// </summary>
public sealed class PermissionAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // A half-authenticated token must never satisfy a permission, however many claims
        // somebody managed to get into it.
        if (!IsFullAccessToken(context))
        {
            return Task.CompletedTask;
        }

        if (currentUser.IsSuperAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var hasPermission = context.User.Claims.Any(claim =>
            string.Equals(claim.Type, ClaimTypeNames.Permission, StringComparison.Ordinal)
            && string.Equals(claim.Value, requirement.PermissionCode, StringComparison.Ordinal));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    internal static bool IsFullAccessToken(AuthorizationHandlerContext context)
    {
        var tokenType = context.User.FindFirst(ClaimTypeNames.TokenType)?.Value;

        // Absent means an older token from before the claim existed; treated as a full access
        // token so a rolling deployment does not lock everybody out.
        return string.IsNullOrWhiteSpace(tokenType)
               || string.Equals(tokenType, nameof(TokenType.Access), StringComparison.Ordinal);
    }
}

/// <summary>Platform-root check, used by the BusinessUnit and Organisation endpoints.</summary>
public sealed class SuperAdminAuthorizationHandler(ICurrentUser currentUser, ITenantContext tenantContext)
    : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        // BOTH conditions, deliberately. The flag says who they are; the scope claim says the
        // token was actually issued for global use. Requiring both means a token minted for a
        // Tenant session cannot be used for platform work even if the user is a root user.
        if (currentUser.IsSuperAdmin && tenantContext.Scope == AccessScopeType.Global)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Organisation-administrator check. SuperAdmin satisfies it too.</summary>
public sealed class TenantAdminAuthorizationHandler(ICurrentUser currentUser, ITenantContext tenantContext)
    : AuthorizationHandler<TenantAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantAdminRequirement requirement)
    {
        if (currentUser.IsSuperAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // A TenantAdmin only counts inside a resolved Organisation. Without one there is
        // nothing for them to administer.
        if (currentUser.IsTenantAdmin && tenantContext.HasTenant)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Requires a resolved Organisation.
///
/// This is what turns "SuperAdmin has not chosen an Organisation yet" into a clean 403 with
/// TENANT_SELECTION_REQUIRED on every Tenant endpoint, rather than each handler discovering
/// the missing context separately and failing in its own way.
/// </summary>
public sealed class TenantContextAuthorizationHandler(ITenantContext tenantContext)
    : AuthorizationHandler<TenantContextRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantContextRequirement requirement)
    {
        if (tenantContext.HasTenant)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Step-up freshness.
///
/// Reads <c>auth_time</c> from the token rather than a session row, so the check costs
/// nothing. It is a coarse gate — the handler for a genuinely sensitive action re-checks
/// against the session's own <c>LastReauthenticatedAtUtc</c>, which moves when the person
/// actually re-authenticates.
/// </summary>
public sealed class RecentReauthenticationHandler(IOptions<JwtSettings> jwtOptions)
    : AuthorizationHandler<RecentReauthenticationRequirement>
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RecentReauthenticationRequirement requirement)
    {
        var authenticatedAt = context.User.FindFirst(ClaimTypeNames.AuthenticatedAt)?.Value;

        if (long.TryParse(authenticatedAt, out var unixSeconds))
        {
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

            if (DateTimeOffset.UtcNow - issuedAt <= TimeSpan.FromMinutes(_jwt.StepUpValidMinutes))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>Refuses an MFA-pending or tenant-selection token on a normal endpoint.</summary>
public sealed class FullAccessTokenHandler : AuthorizationHandler<FullAccessTokenRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, FullAccessTokenRequirement requirement)
    {
        if (PermissionAuthorizationHandler.IsFullAccessToken(context))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Segregation of duties at the route level.
///
/// This is the COARSE half: it stops a caller acting on a record whose route id is their own
/// user id. The real maker-checker test — "did you raise this request?" — needs the record
/// itself and lives in the handler. Both are needed; neither alone is enough.
/// </summary>
public sealed class IndependentActorHandler(
    Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<IndependentActorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, IndependentActorRequirement requirement)
    {
        var callerId = context.User.FindFirst(ClaimTypeNames.UserId)?.Value
                       ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var routeValue = httpContextAccessor.HttpContext?.Request.RouteValues
            .TryGetValue(requirement.RouteValueName, out var value) == true
            ? value?.ToString()
            : null;

        // No subject on the route means the rule cannot be broken here.
        if (routeValue is null || callerId is null
            || !string.Equals(routeValue, callerId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Creates a policy on demand for any permission code.
///
/// Because of this class a new permission never needs a startup registration: decorate the
/// endpoint with <c>[HasPermission("iam.users.create")]</c> and the policy appears. That is
/// what keeps a hundred and thirty codes from becoming a hundred and thirty lines of
/// configuration that somebody eventually forgets to add to.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = await base.GetPolicyAsync(policyName);
        if (policy is not null)
        {
            return policy;
        }

        if (!policyName.StartsWith(PolicyNames.PermissionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var permissionCode = policyName[PolicyNames.PermissionPrefix.Length..];

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissionCode))
            .Build();
    }
}

/// <summary>
/// Convenience attribute. <c>[HasPermission(PermissionCodes.UsersCreate)]</c> on an action is
/// all that is needed to enforce a permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permissionCode) => Policy = PolicyNames.ForPermission(permissionCode);
}

/// <summary>
/// Marks an endpoint as reachable while the Organisation is still onboarding.
///
/// HOW A MARKER ATTRIBUTE WORKS, if this is new to you: writing <c>[AllowedWhileOnboarding]</c>
/// above a controller or one of its methods attaches a small label to it. The label does
/// nothing by itself - no code runs when you write it. Something else has to go looking for
/// it, and here that something is <c>OrganisationApprovalMiddleware</c>, which reads the label
/// off the endpoint the request matched and uses it to decide whether to let the request past.
///
/// EVERYTHING ELSE IS REFUSED UNTIL THE ORGANISATION IS APPROVED. <c>OrganisationApproval
/// Middleware</c> denies by default and consults this attribute for the exceptions, so a
/// controller added later is blocked during onboarding until somebody decides otherwise —
/// which is the safe direction to fail. Hiding the menu item is not enough on its own: the
/// TenantAdmin role carries <c>GrantsAllTenantPermissions</c> from the moment the Organisation
/// is created, so a hand-typed URL would otherwise reach an endpoint that answers 200.
///
/// USE IT SPARINGLY, and only for work that onboarding genuinely cannot proceed without:
/// reading and editing the Organisation's own profile, attaching its registration documents,
/// submitting for approval, and the account screens a person needs whatever their
/// Organisation's status is.
///
/// <c>[AllowAnonymous]</c> endpoints — sign-in, invitation acceptance, password reset — need
/// no attribute. The middleware never reaches them, because an unapproved Organisation still
/// has to be able to let its administrator in.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowedWhileOnboardingAttribute : Attribute;
