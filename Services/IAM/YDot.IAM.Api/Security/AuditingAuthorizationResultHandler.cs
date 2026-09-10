using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Security;

/// <summary>
/// Records an authorisation refusal in the audit trail, then lets the default handler answer.
///
/// WHY IT IS A RESULT HANDLER AND NOT A FILTER. This is the ONE place every refusal passes
/// through: policy evaluation happens in the authorization middleware, before model binding and
/// before any action filter, so an endpoint refused on a permission never reaches a filter that
/// could have noticed. Everything else that could record it - a handler, a filter, a controller
/// base class - is downstream of the decision and simply does not run.
///
/// THAT IS WHY THE TRAIL ONLY EVER SHOWED "Succeeded". A 403 left no row at all, so the outcome
/// the audit screen exists to surface - somebody attempting what they are not allowed to do - was
/// the one outcome it could never show.
///
/// ONLY A REFUSAL IS RECORDED, NOT A CHALLENGE. An unauthenticated caller is a 401 and means
/// "sign in", which is the ordinary consequence of an expired token and would fill the trail with
/// noise. A caller who IS authenticated and is still refused is the interesting case, and that is
/// what <see cref="AuthorizationPolicy"/> evaluation reports as Forbidden.
///
/// IT NEVER CHANGES THE RESPONSE. The default handler decides the status code exactly as before;
/// this only writes a row on the way past. A failure inside the auditor is swallowed by the
/// auditor itself, so a trail that cannot be written can never turn a 403 into a 500.
/// </summary>
public sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden)
        {
            var auditor = context.RequestServices.GetService<IRequestOutcomeAuditor>();

            if (auditor is not null)
            {
                await auditor.RecordDeniedAsync(
                    context.Request.Method,
                    context.Request.Path.Value ?? string.Empty,
                    DescribeRefusal(context, policy, authorizeResult),
                    context.RequestAborted);
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// What the caller was refused ON, in words somebody investigating can use.
    ///
    /// THE PERMISSION CODE IS THE USEFUL PART. "Forbidden" says nothing an investigation can act
    /// on; "iam.users.suspend" says exactly which grant is missing, which is the difference
    /// between a row worth reading and a row worth ignoring. The requirements on the policy are
    /// where that code lives, so they are unpacked here rather than summarised as a policy name.
    /// </summary>
    private static string DescribeRefusal(
        HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var permissions = policy.Requirements
            .OfType<PermissionRequirement>()
            .Select(requirement => requirement.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (permissions.Count > 0)
        {
            return $"Refused. The caller does not hold {string.Join(", ", permissions)}.";
        }

        // No permission requirement means one of the shaped policies - SuperAdminOnly,
        // TenantAdminOnly, a recent re-authentication, an independent actor. Naming the
        // requirement types is the most specific thing available without inventing a label per
        // policy that would then have to be kept in step with the policy list.
        var requirements = policy.Requirements
            .Select(requirement => requirement.GetType().Name.Replace("Requirement", string.Empty, StringComparison.Ordinal))
            .Where(name => !string.Equals(name, "DenyAnonymousAuthorization", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var failed = authorizeResult.AuthorizationFailure?.FailedRequirements
            .Select(requirement => requirement.GetType().Name.Replace("Requirement", string.Empty, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        var named = failed.Count > 0 ? failed : requirements;

        return named.Count > 0
            ? $"Refused by the {string.Join(", ", named)} rule on {context.Request.Path}."
            : $"Refused by the authorization policy on {context.Request.Path}.";
    }
}
