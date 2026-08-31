using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.SelectTenant;

/// <summary>Section 13. <c>POST /api/v1/auth/select-tenant</c>.</summary>
public sealed record SelectTenantCommand(SelectTenantRequest Request);

/// <summary>Lists the Organisations the caller may enter.</summary>
public sealed record GetSelectableTenantsQuery;

/// <summary>
/// Section 13. <c>POST /api/v1/auth/exit-tenant</c>. The way back out of an Organisation.
/// </summary>
public sealed record ExitTenantCommand;

/// <summary>
/// SuperAdmin entering an Organisation operating context.
///
/// THE ONE RULE THIS HANDLER EXISTS TO PROTECT. Section 4.1 of the brief:
///
///     "Selecting a Tenant must NOT modify SuperAdmin persistent User.TenantId."
///
/// So nothing here writes to the user row. What changes is the TOKEN and the SESSION row:
///
/// <code>
/// before        sub = U001   scope = Global   tenant_id = (none)
/// after TEN001  sub = U001   scope = Global   tenant_id = TEN001   tenant_mode = true
/// after TEN002  sub = U001   scope = Global   tenant_id = TEN002   tenant_mode = true
/// </code>
///
/// <c>User.TenantId</c> stays NULL throughout. That is what keeps SuperAdmin a root user who
/// visits Organisations rather than a member of the last one they happened to open.
///
/// WHY THE SAME SESSION IS REUSED. Switching Organisation is not a new sign-in — the person
/// has not re-proved anything, and minting a fresh session each time would litter their
/// security screen with entries and break "sign out everywhere". The session row simply
/// records the new operating Organisation, which is also what lets the audit trail say which
/// Organisation a root user was standing in when they acted.
///
/// AUTHORISATION IS CHECKED HERE, NOT ONLY ON THE ROUTE. The endpoint carries the
/// <c>platform.organisations.select</c> permission, but this handler re-checks that the caller
/// is genuinely Global scope. A permission can be mis-mapped onto a Tenant role by an
/// over-enthusiastic role edit; the scope claim cannot, because only this service issues it.
/// </summary>
public sealed class SelectTenantCommandHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ISessionTokenService sessions,
    IEffectiveAccessService effectiveAccess,
    IAuditService audit,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<SelectTenantResponse>> HandleAsync(
        SelectTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ---- Only a global caller may do this ------------------------------------------------
        if (!currentUser.IsSuperAdmin || tenantContext.Scope != AccessScopeType.Global)
        {
            await audit.WriteAsync(
                AuditActionCodes.TenantSelected, nameof(Tenant), command.Request.TenantId,
                AuditResult.Denied, null,
                new { Reason = "Caller is not global scope." },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SelectTenantResponse>(Error.SuperAdminOnly());
        }

        if (currentUser.SessionId is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.SessionExpired());
        }

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = await tenants.GetByIdAsync(command.Request.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.TenantNotFound());
        }

        // A root user may enter an Organisation that is still onboarding — reviewing a
        // submission is exactly why they would — but not one that has been archived, where
        // there is nothing to operate on.
        if (tenant.Status == TenantStatus.Archived)
        {
            return Result.Failure<SelectTenantResponse>(
                Error.TenantInactive("That organisation is archived and cannot be entered."));
        }

        var user = await users.GetWithAccessAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.Unauthorised());
        }

        // Re-issue the access token against the SAME session, now pointed at this Organisation.
        var tokens = await sessions.ReissueForTenantAsync(
            currentUser.SessionId.Value, user, tenant, businessUnit,
            tenantContext.HostName, cancellationToken);

        // Effective access is resolved INSIDE the selected Organisation. For SuperAdmin the
        // permission set is unrestricted either way, but resolving it here means the menu and
        // the preview screens see the same Organisation the token now names.
        var access = await effectiveAccess.ResolveAsync(user, tenant.Id, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantSelected, nameof(Tenant), tenant.Id, tenant.Name,
            new
            {
                PreviousTenantId = tenantContext.TenantId,
                SelectedTenantId = tenant.Id,
                tenant.Code,
                // Recorded explicitly so the trail proves the invariant was honoured.
                SuperAdminTenantIdUnchanged = user.TenantId is null
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new SelectTenantResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.TokenType,
            tokens.SessionId,
            AuthenticationMappingConfig.ToTenantContext(
                tenant, businessUnit, AccessScopeType.Global, isTenantMode: true),
            AuthenticationMappingConfig.ToAuthenticatedUser(user, access)));
    }

    /// <summary>
    /// Leaves the current Organisation and returns the caller to platform scope.
    ///
    /// THE MISSING HALF OF <see cref="SelectTenantCommand"/>. Entering an Organisation was a
    /// one-way door: nothing cleared the operating Organisation again, so a root user who opened
    /// one carried its tenant_id for the rest of the session and could only shed it by signing
    /// out. Since tenant_id stamps writes, filters queries and labels audit rows, that is a
    /// correctness problem and not merely a navigation annoyance.
    ///
    /// LIKE SELECTING, IT TOUCHES ONLY THE TOKEN AND THE SESSION. User.TenantId is NULL for a
    /// root user before this call and after it; the invariant from section 4.1 is untouched.
    ///
    /// IT IS DELIBERATELY FORGIVING when there is nothing to leave. Somebody already at platform
    /// scope gets a fresh platform token and a success, not a 409: the caller asked to end up
    /// outside an Organisation, and they are.
    /// </summary>
    public async Task<Result<SelectTenantResponse>> HandleAsync(
        ExitTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Only a global caller has anywhere to go back to. A Tenant user asking to leave their
        // own Organisation is asking for something that does not exist.
        if (!currentUser.IsSuperAdmin || tenantContext.Scope != AccessScopeType.Global)
        {
            await audit.WriteAsync(
                AuditActionCodes.TenantSelected, nameof(Tenant), tenantContext.TenantId ?? Guid.Empty,
                AuditResult.Denied, null,
                new { Reason = "Caller is not global scope." },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<SelectTenantResponse>(Error.SuperAdminOnly());
        }

        if (currentUser.SessionId is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.SessionExpired());
        }

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.Dependency("The platform is not configured."));
        }

        var user = await users.GetWithAccessAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<SelectTenantResponse>(Error.Unauthorised());
        }

        var previousTenantId = tenantContext.TenantId;

        var tokens = await sessions.ReissueForPlatformAsync(
            currentUser.SessionId.Value, user, businessUnit, tenantContext.HostName, cancellationToken);

        var access = await effectiveAccess.ResolveAsync(user, null, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantSelected, nameof(Tenant), previousTenantId ?? Guid.Empty, null,
            new
            {
                PreviousTenantId = previousTenantId,
                SelectedTenantId = (Guid?)null,
                Action = "ExitedToPlatformScope",
                SuperAdminTenantIdUnchanged = user.TenantId is null
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new SelectTenantResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.TokenType,
            tokens.SessionId,
            AuthenticationMappingConfig.ToTenantContext(
                null, businessUnit, AccessScopeType.Global, isTenantMode: false),
            AuthenticationMappingConfig.ToAuthenticatedUser(user, access)));
    }

    /// <summary>
    /// The Organisations offered in the switcher.
    ///
    /// Only a global caller gets a list. A Tenant user gets an empty one rather than a 403,
    /// because the switcher is simply not part of their interface and an error would imply
    /// there is something there to reach.
    /// </summary>
    public async Task<Result<IReadOnlyList<TenantOptionResponse>>> HandleAsync(
        GetSelectableTenantsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdmin || tenantContext.Scope != AccessScopeType.Global)
        {
            return Result.Success<IReadOnlyList<TenantOptionResponse>>([]);
        }

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Success<IReadOnlyList<TenantOptionResponse>>([]);
        }

        var selectable = await tenants.GetSelectableAsync(businessUnit.Id, cancellationToken);

        return Result.Success<IReadOnlyList<TenantOptionResponse>>(
            [.. selectable.Select(AuthenticationMappingConfig.ToTenantOption)]);
    }
}
