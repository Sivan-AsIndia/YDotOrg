using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Constants;

namespace YDots.DON.Infrastructure.Authorization;

/// <summary>
/// Creates a policy on demand for any permission code.
///
/// Because of this class you never have to register a policy for a new permission: decorate the
/// endpoint with [HasPermission("don.donors.create")] and the policy appears automatically. This
/// is the extension point that keeps the section's 49 codes from becoming 49 lines of startup
/// configuration.
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
/// Convenience attribute. [HasPermission(PermissionCodes.DonorsCreate)] on a controller action
/// is all that is needed to enforce a permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permissionCode) => Policy = PolicyNames.ForPermission(permissionCode);
}
