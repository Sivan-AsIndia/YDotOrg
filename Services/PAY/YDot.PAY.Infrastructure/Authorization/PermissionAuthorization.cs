using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Constants;

namespace YDot.PAY.Infrastructure.Authorization;

/// <summary>The requirement behind a <c>[HasPermission("pay.refunds.approve")]</c> attribute.</summary>
public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>Caller must carry a resolved Organisation context.</summary>
public sealed class TenantContextRequirement : IAuthorizationRequirement;

/// <summary>Caller must be the platform root user.</summary>
public sealed class SuperAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Decorates an endpoint with the permission it requires.
///
/// The policy name is manufactured from the code by <see cref="PermissionPolicyProvider"/>, so
/// adding a permission never needs a startup registration - which is what keeps thirty-odd codes
/// from becoming thirty lines of configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permissionCode)
    : AuthorizeAttribute(PolicyNames.ForPermission(permissionCode))
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>
/// Answers a permission requirement from the claims on the token.
///
/// It defers to <see cref="ICurrentUser.HasPermission"/> rather than reading the claims itself,
/// so the SuperAdmin rule lives in exactly one place. Two copies of "does this caller hold this
/// permission?" is one that will eventually answer differently from the other - and in this
/// service the difference would be who may move money.
/// </summary>
public sealed class PermissionAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (currentUser.HasPermission(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        // NOT Fail(). Failing would veto the whole policy even if another handler could satisfy
        // it; simply not succeeding lets the framework fall through to a 403, which is the answer
        // we want.
        return Task.CompletedTask;
    }
}

/// <summary>Answers "does this request have an Organisation?".</summary>
public sealed class TenantContextAuthorizationHandler(ITenantContext tenantContext)
    : AuthorizationHandler<TenantContextRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantContextRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A PUBLIC DONOR CONTEXT DOES NOT SATISFY THIS. It resolves a real Organisation, so
        // HasTenant is true - but it was resolved from a reference the caller supplied, and an
        // endpoint that asked for a tenant context is a staff endpoint. Letting a payment link
        // stand in for a token here would open every one of them to anybody holding a link.
        if (tenantContext.HasTenant && !tenantContext.IsPublicDonorContext)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Answers "is this the platform root user?".</summary>
public sealed class SuperAdminAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (currentUser.IsSuperAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Manufactures a policy for any permission code on demand.
///
/// Because of this class a new permission never needs a startup registration: decorate the
/// endpoint with <c>[HasPermission("pay.refunds.approve")]</c> and the policy is built the first
/// time that route is hit. The alternative is one <c>AddPolicy</c> line per code, which is a list
/// somebody eventually forgets to extend - and the failure mode there is an endpoint that throws
/// "no policy found" rather than one that refuses the caller.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // A named policy registered at startup wins, so ActiveUserOnly and friends still work.
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
