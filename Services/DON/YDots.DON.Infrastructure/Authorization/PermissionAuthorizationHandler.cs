using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;

namespace YDots.DON.Infrastructure.Authorization;

/// <summary>
/// Permission based authorization. The JWT that IAM issued already carries one permission claim
/// per code, so the check is a claim lookup: no database call and no cross-service call on the
/// hot path. That is the whole reason DON can enforce IAM's permissions without talking to IAM.
///
/// It defers to <see cref="ICurrentUser.HasPermission"/> rather than reading the claims itself,
/// so the SuperAdmin rule lives in exactly one place. This handler used to compare the claims
/// directly, and because IAM issues the platform root user NO permission claims at all - global
/// scope short-circuits the lookup on that side rather than enumerating fifty-odd codes onto the
/// token - every single donor and lead endpoint answered 403 to SuperAdmin, while the same token
/// passed straight through IAM, CAM and PAY. <c>CurrentUser.HasPermission</c> already carried the
/// bypass; the pipeline simply never asked it.
/// </summary>
public sealed class PermissionAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (currentUser.HasPermission(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        // NOT Fail(). Failing would veto the whole policy even if another handler could satisfy
        // it; simply not succeeding lets the framework fall through to a 403, which is the
        // answer we want.
        return Task.CompletedTask;
    }
}

/// <summary>Data scope. Every caller must carry an organisation claim before any query runs.</summary>
public sealed class SameOrganisationHandler : AuthorizationHandler<SameOrganisationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameOrganisationRequirement requirement)
    {
        var organisationId = context.User.FindFirst(ClaimTypeNames.OrganisationId)?.Value;

        if (!string.IsNullOrWhiteSpace(organisationId) && Guid.TryParse(organisationId, out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Segregation of duties at the route level.
///
/// This is the coarse half of the rule: it stops a caller acting on a record whose id equals
/// their own user id. The real maker/checker test — "did you create this donor?" — needs the
/// record itself, so it lives in DonorCommandHandler.HandleAsync(ApproveDonorCommand).
/// </summary>
public sealed class SegregationOfDutiesHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<SegregationOfDutiesRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SegregationOfDutiesRequirement requirement)
    {
        var callerId = context.User.FindFirst(ClaimTypeNames.UserId)?.Value
                       ?? context.User.FindFirst("sub")?.Value;

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
