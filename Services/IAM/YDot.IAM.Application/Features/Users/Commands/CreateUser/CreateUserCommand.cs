using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Users.Commands.CreateUser;

/// <summary>IAM-USR-01. Creates a user in the caller Organisation and invites them.</summary>
public sealed record CreateUserCommand(CreateUserRequest Request);

/// <summary>Live availability check for the create form.</summary>
public sealed record CheckUserIdentityQuery(CheckUserIdentityRequest Request);

/// <summary>Re-sends an outstanding invitation with a fresh token.</summary>
public sealed record ResendUserInvitationCommand(Guid UserId, string? Message);

/// <summary>Withdraws an outstanding invitation.</summary>
public sealed record RevokeUserInvitationCommand(Guid UserId, string Reason);

/// <summary>
/// User creation.
///
/// THE UNIQUENESS RULE IS THE INTERESTING PART. E-mail and username are checked against THIS
/// Organisation only. The same address existing in another Organisation is not a conflict —
/// it is the documented behaviour from section 6 of the brief, and two such users are
/// genuinely separate people with separate passwords and separate roles.
///
/// A NEW USER HAS NO PASSWORD. The account is created with a null hash and status Invited, so
/// it genuinely cannot be signed into rather than relying on a status check that somebody
/// might one day forget. The invitation is what turns it into a usable account.
/// </summary>
public sealed class CreateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IInvitationRepository invitations,
    IGovernanceRepository governance,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<EmailSettings> emailOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    /// <summary>
    /// Whether an e-mail address or username is free INSIDE THIS ORGANISATION.
    ///
    /// The scoping is the whole point. john@example.com may exist in three Organisations at
    /// once, and none of those is a clash; what must never happen is two of them inside one.
    /// The repository lookups below are Organisation-scoped by the query filter, so the answer
    /// is about the caller's Organisation and no other.
    ///
    /// IT NEVER SAYS WHO HOLDS A TAKEN VALUE. That would turn the create form into a directory
    /// lookup for anybody who can reach it.
    /// </summary>
    public async Task<Result<CheckUserIdentityResponse>> HandleAsync(
        CheckUserIdentityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var request = query.Request;
        var email = request.Email?.Trim();
        var username = request.Username?.Trim();

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(username))
        {
            return Result.Success(new CheckUserIdentityResponse(
                IsAvailable: true, EmailAvailable: true, UsernameAvailable: true,
                Message: "Enter an e-mail address or a username to check.",
                Suggestions: []));
        }

        var emailAvailable = true;
        var usernameAvailable = true;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var parsed = EmailValue.TryParse(email);

            if (parsed is null)
            {
                return Result.Success(new CheckUserIdentityResponse(
                    IsAvailable: false, EmailAvailable: false, UsernameAvailable: true,
                    Message: "That is not a valid e-mail address.",
                    Suggestions: []));
            }

            emailAvailable = !await users.EmailExistsAsync(
                parsed.Value.ToUpperInvariant(), tenantContext.TenantId, request.ExcludeUserId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var parsed = UsernameValue.TryParse(username);

            if (parsed is null)
            {
                return Result.Success(new CheckUserIdentityResponse(
                    IsAvailable: false, EmailAvailable: emailAvailable, UsernameAvailable: false,
                    Message: "A username may use letters, digits, dots, hyphens and underscores.",
                    Suggestions: []));
            }

            usernameAvailable = !await users.UsernameExistsAsync(
                parsed.Value.ToUpperInvariant(), tenantContext.TenantId, request.ExcludeUserId, cancellationToken);
        }

        // Suggestions only for a taken USERNAME. Suggesting variations of somebody's e-mail
        // address would be both useless and slightly alarming.
        var suggestions = usernameAvailable || string.IsNullOrWhiteSpace(username)
            ? []
            : await BuildUsernameSuggestionsAsync(username, cancellationToken);

        var available = emailAvailable && usernameAvailable;

        return Result.Success(new CheckUserIdentityResponse(
            available,
            emailAvailable,
            usernameAvailable,
            available
                ? "That is available."
                : !emailAvailable && !usernameAvailable
                    ? "Both the e-mail address and the username are already in use in this organisation."
                    : !emailAvailable
                        ? "Somebody in this organisation already uses that e-mail address."
                        : "That username is already in use in this organisation.",
            suggestions));
    }

    /// <summary>
    /// Up to three free variations on a taken username.
    ///
    /// Each candidate is checked rather than merely generated, so the form never offers one that
    /// is itself taken - which is a worse experience than offering nothing at all.
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildUsernameSuggestionsAsync(
        string username, CancellationToken cancellationToken)
    {
        var stem = username.Trim().ToLowerInvariant();
        var found = new List<string>(3);

        for (var suffix = 1; suffix <= 20 && found.Count < 3; suffix++)
        {
            var candidate = $"{stem}{suffix}";
            var parsed = UsernameValue.TryParse(candidate);

            if (parsed is null)
            {
                continue;
            }

            var taken = await users.UsernameExistsAsync(
                parsed.Value.ToUpperInvariant(), tenantContext.TenantId, null, cancellationToken);

            if (!taken)
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly EmailSettings _email = emailOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    public async Task<Result<CreateUserResponse>> HandleAsync(
        CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        // The Organisation comes from the request context, never from the body.
        if (!tenantContext.HasTenant)
        {
            return Result.Failure<CreateUserResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<CreateUserResponse>(Error.TenantNotFound());
        }

        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<CreateUserResponse>(Error.Dependency("The platform is not configured."));
        }

        // ---- Licence ceiling -------------------------------------------------------------
        if (tenant.MaximumUsers.HasValue)
        {
            var existing = await users.CountActiveAsync(tenantId, cancellationToken);
            if (existing >= tenant.MaximumUsers.Value)
            {
                return Result.Failure<CreateUserResponse>(Error.UserLimitReached());
            }
        }

        // ---- Identity, scoped to this Organisation ------------------------------------------
        var email = EmailValue.TryParse(request.Email);
        if (email is null)
        {
            return Result.Failure<CreateUserResponse>(
                Error.Validation("Enter a valid e-mail address.",
                    [new ValidationError(nameof(request.Email), "That e-mail address is not valid.")]));
        }

        if (await users.EmailExistsAsync(email.Value.ToUpperInvariant(), tenantId, null, cancellationToken))
        {
            return Result.Failure<CreateUserResponse>(
                Error.Duplicate("Somebody in this organisation already uses that e-mail address."));
        }

        var usernameCandidate = string.IsNullOrWhiteSpace(request.Username)
            ? email.LocalPart
            : request.Username;

        var username = UsernameValue.TryParse(usernameCandidate);
        if (username is null)
        {
            return Result.Failure<CreateUserResponse>(
                Error.Validation("That username is not valid.",
                    [new ValidationError(nameof(request.Username),
                        "Use 3 to 64 letters, digits, dots, hyphens or underscores.")]));
        }

        // A collision on the derived username is resolved rather than reported: the person
        // did not choose it, so refusing the whole create would be baffling.
        var finalUsername = username.Value;
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            var suffix = 1;
            while (await users.UsernameExistsAsync(
                       finalUsername.ToUpperInvariant(), tenantId, null, cancellationToken))
            {
                finalUsername = $"{username.Value}{suffix++}";
                if (suffix > 999)
                {
                    break;
                }
            }
        }
        else if (await users.UsernameExistsAsync(finalUsername.ToUpperInvariant(), tenantId, null, cancellationToken))
        {
            return Result.Failure<CreateUserResponse>(
                Error.Duplicate("Somebody in this organisation already uses that username."));
        }

        // ---- Referential checks --------------------------------------------------------------
        //
        // The query filter means a department in another Organisation simply is not found,
        // which is the correct answer and also the safe one.
        if (request.ManagerUserId.HasValue)
        {
            var manager = await users.GetByIdAsync(request.ManagerUserId.Value, cancellationToken);
            if (manager is null)
            {
                return Result.Failure<CreateUserResponse>(
                    Error.Validation("That manager was not found in this organisation.",
                        [new ValidationError(nameof(request.ManagerUserId), "Choose a manager from this organisation.")]));
            }
        }

        // ---- The user ---------------------------------------------------------------------------
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{request.FirstName.Trim()} {request.LastName.Trim()}".Trim()
            : request.DisplayName.Trim();

        var user = new User
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnit.Id,
            Code = await users.NextUserCodeAsync(tenantId, cancellationToken),
            EmployeeNumber = request.EmployeeNumber?.Trim(),
            FirstName = request.FirstName.Trim(),
            MiddleName = request.MiddleName?.Trim(),
            LastName = request.LastName.Trim(),
            DisplayName = displayName,
            Email = email.Value,
            NormalizedEmail = email.Value.ToUpperInvariant(),
            UserName = finalUsername,
            NormalizedUserName = finalUsername.ToUpperInvariant(),
            EmailConfirmed = false,
            MobileCountryCode = request.MobileCountryCode?.Trim(),
            MobileNumber = request.MobileNumber?.Trim(),
            AccountCategory = request.AccountCategory,
            EngagementType = request.EngagementType,
            DepartmentId = request.DepartmentId,
            OrganisationUnitId = request.OrganisationUnitId,
            Designation = request.Designation?.Trim(),
            ManagerUserId = request.ManagerUserId,
            // Draft when no invitation is being sent, so a staged import does not look like a
            // pile of people who were invited and never replied.
            Status = request.SendInvitation ? UserStatus.Invited : UserStatus.Draft,
            AccessStartsAtUtc = request.AccessStartsAtUtc ?? now,
            AccessEndsAtUtc = request.AccessEndsAtUtc,
            MfaRequirement = request.MfaRequirement,
            JoinedOn = request.JoinedOn,
            CredentialSetupMethod = request.CredentialSetupMethod,
            PrivilegeLevel = PrivilegeLevel.Standard,
            LockoutEnabled = true,
            IsSuperAdmin = false,
            IsTenantAdmin = false
        };

        user.PhoneNumber = user.ToE164();

        // An administrator-set temporary password. Usable immediately, and must be changed at
        // first sign-in.
        string? temporaryPassword = null;
        if (request.CredentialSetupMethod == CredentialSetupMethod.TemporaryPassword)
        {
            temporaryPassword = passwordHasher.GenerateTemporaryPassword();
            user.PasswordHash = passwordHasher.Hash(temporaryPassword);
            user.MustChangePassword = true;
            user.Status = UserStatus.Active;
        }

        await users.AddAsync(user, cancellationToken);

        // ---- Roles ------------------------------------------------------------------------------
        var assigned = await AssignRolesAsync(user, tenant, request.RoleIds, now, cancellationToken);
        if (assigned.IsFailure)
        {
            return Result.Failure<CreateUserResponse>(assigned.Error!);
        }

        // ---- Narrowing scopes ---------------------------------------------------------------------
        foreach (var scope in request.DataScopes ?? [])
        {
            await governance.AddDataScopeAsync(new UserDataScope
            {
                TenantId = tenantId,
                BusinessUnitId = businessUnit.Id,
                UserId = user.Id,
                ScopeType = scope.ScopeType,
                ScopeValue = scope.ScopeValue.Trim(),
                DisplayLabel = scope.DisplayLabel,
                GrantedAtUtc = now,
                GrantedByUserId = currentUser.UserId,
                EffectiveFromUtc = now,
                EffectiveToUtc = scope.EffectiveToUtc
            }, cancellationToken);
        }

        // ---- Invitation -------------------------------------------------------------------------------
        UserInvitation? invitation = null;
        string? plaintextToken = null;

        if (request.SendInvitation && request.CredentialSetupMethod == CredentialSetupMethod.InvitationLink)
        {
            plaintextToken = tokenHasher.GenerateToken();
            var primaryDomain = await tenants.GetPrimaryDomainAsync(tenantId, cancellationToken);

            invitation = new UserInvitation
            {
                TenantId = tenantId,
                BusinessUnitId = businessUnit.Id,
                UserId = user.Id,
                Email = email.Value,
                NormalizedEmail = email.Value.ToUpperInvariant(),
                InvitationType = InvitationType.TenantUser,
                InitialRoleId = request.RoleIds is { Count: > 0 } roleIds ? roleIds[0] : null,
                TokenHash = tokenHasher.Hash(plaintextToken),
                Reference = tokenHasher.GenerateReference("INV"),
                ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays),
                Status = InvitationStatus.Pending,
                InvitedByUserId = currentUser.UserId,
                InvitedAtUtc = now,
                InvitationHostName = primaryDomain?.HostName ?? $"{tenant.Subdomain}.{businessUnit.RootDomain}",
                Message = request.InvitationMessage,
                LastSentAtUtc = now
            };

            await invitations.AddAsync(invitation, cancellationToken);
        }

        await audit.WriteAsync(
            AuditActionCodes.UserCreated, nameof(User), user.Id, user.DisplayName,
            new
            {
                user.Code,
                Email = email.Value,
                user.AccountCategory,
                RoleCount = request.RoleIds?.Count ?? 0,
                request.SendInvitation
            },
            cancellationToken: cancellationToken);

        if (invitation is not null)
        {
            await audit.WriteAsync(
                AuditActionCodes.UserInvited, nameof(UserInvitation), invitation.Id,
                user.DisplayName, new { invitation.Reference },
                cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Sent after the commit. A mail relay failure must not undo a user who has already
        // been created; the invitation can simply be re-sent.
        string? activationUrl = null;
        if (invitation is not null && plaintextToken is not null)
        {
            activationUrl = BuildActivationUrl(invitation.InvitationHostName!, plaintextToken);

            await notifications.SendInvitationAsync(
                user, invitation, tenant, businessUnit, activationUrl, cancellationToken);
        }

        return Result.Success(new CreateUserResponse(
            user.Id,
            user.Code,
            user.DisplayName,
            user.Email!,
            user.Status,
            invitation is not null,
            invitation?.ExpiresAtUtc,
            _email.Enabled ? null : activationUrl,
            user.Version));
    }

    public async Task<Result<CreateUserResponse>> HandleAsync(
        ResendUserInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<CreateUserResponse>(Error.UserNotFound());
        }

        if (user.Status is not (UserStatus.Invited or UserStatus.Draft))
        {
            return Result.Failure<CreateUserResponse>(Error.InvalidTransition(
                "That account is already active. There is nothing to resend."));
        }

        var tenant = user.TenantId.HasValue
            ? await tenants.GetByIdAsync(user.TenantId.Value, cancellationToken)
            : null;

        var businessUnit = await businessUnits.GetByIdAsync(user.BusinessUnitId, cancellationToken);
        if (businessUnit is null || tenant is null)
        {
            return Result.Failure<CreateUserResponse>(Error.Dependency("The platform is not configured."));
        }

        var invitation = await invitations.GetPendingForUserAsync(user.Id, cancellationToken);
        var plaintextToken = tokenHasher.GenerateToken();
        var primaryDomain = await tenants.GetPrimaryDomainAsync(tenant.Id, cancellationToken);
        var hostName = primaryDomain?.HostName ?? $"{tenant.Subdomain}.{businessUnit.RootDomain}";

        if (invitation is null)
        {
            // The account was created without one, or the previous invitation was revoked.
            invitation = new UserInvitation
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                UserId = user.Id,
                Email = user.Email!,
                NormalizedEmail = user.NormalizedEmail!,
                InvitationType = InvitationType.TenantUser,
                TokenHash = tokenHasher.Hash(plaintextToken),
                Reference = tokenHasher.GenerateReference("INV"),
                ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays),
                Status = InvitationStatus.Pending,
                InvitedByUserId = currentUser.UserId,
                InvitedAtUtc = now,
                InvitationHostName = hostName,
                Message = command.Message,
                LastSentAtUtc = now
            };

            await invitations.AddAsync(invitation, cancellationToken);
        }
        else
        {
            // A fresh token. The old one stops working, so an invitation forwarded to the
            // wrong person months ago is not still live alongside the new one.
            invitation.TokenHash = tokenHasher.Hash(plaintextToken);
            invitation.ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays);
            invitation.Status = InvitationStatus.Resent;
            invitation.ResendCount += 1;
            invitation.LastSentAtUtc = now;
            invitation.InvitationHostName = hostName;
            invitation.Message = command.Message ?? invitation.Message;
        }

        user.Status = UserStatus.Invited;

        await audit.WriteAsync(
            AuditActionCodes.UserInvitationResent, nameof(UserInvitation), invitation.Id,
            user.DisplayName, new { invitation.ResendCount },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var activationUrl = BuildActivationUrl(hostName, plaintextToken);

        await notifications.SendInvitationAsync(
            user, invitation, tenant, businessUnit, activationUrl, cancellationToken);

        return Result.Success(new CreateUserResponse(
            user.Id, user.Code, user.DisplayName, user.Email!, user.Status,
            InvitationSent: true, invitation.ExpiresAtUtc,
            _email.Enabled ? null : activationUrl, user.Version));
    }

    /// <summary>
    /// Withdraws an outstanding invitation. The token stops working immediately, which is the
    /// remedy when one was sent to the wrong address.
    /// </summary>
    public async Task<Result<DTOs.CreateUserResponse>> HandleAsync(
        RevokeUserInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<CreateUserResponse>(Error.UserNotFound());
        }

        var invitation = await invitations.GetPendingForUserAsync(user.Id, cancellationToken);
        if (invitation is null)
        {
            return Result.Failure<CreateUserResponse>(
                Error.NotFound("There is no outstanding invitation for that user."));
        }

        var now = clock.UtcNow;
        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAtUtc = now;
        invitation.RevokedByUserId = currentUser.UserId;
        invitation.RevocationReason = command.Reason;

        // The token hash is scrambled as well as the status changed, so a leaked link is
        // dead even if some future code path forgets to check the status.
        invitation.TokenHash = tokenHasher.Hash(tokenHasher.GenerateToken());

        user.Status = UserStatus.Draft;

        await audit.WriteAsync(
            AuditActionCodes.UserInvitationRevoked, nameof(UserInvitation), invitation.Id,
            user.DisplayName, new { command.Reason }, command.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateUserResponse(
            user.Id, user.Code, user.DisplayName, user.Email!, user.Status,
            InvitationSent: false, null, null, user.Version));
    }

    /// <summary>
    /// Grants the requested roles, or the Organisation default when none was named.
    ///
    /// Refuses a combination that breaks a blocking segregation-of-duties rule, at the point
    /// somebody tries to create it rather than at the next audit.
    /// </summary>
    private async Task<Result> AssignRolesAsync(
        User user, Tenant tenant, IReadOnlyList<Guid>? roleIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requested = roleIds is { Count: > 0 }
            ? await roles.GetManyAsync(roleIds, cancellationToken)
            : [];

        if (roleIds is { Count: > 0 } && requested.Count != roleIds.Count)
        {
            return Result.Failure(Error.Validation(
                "One or more of those roles was not found in this organisation.",
                [new ValidationError("RoleIds", "Choose roles from this organisation.")]));
        }

        // Fall back to the default, so a new user is not created able to sign in and see
        // nothing — which reads as a broken account rather than a missing role.
        if (requested.Count == 0)
        {
            var fallback = await roles.GetDefaultRoleAsync(tenant.Id, cancellationToken);
            if (fallback is not null)
            {
                requested = [fallback];
            }
        }

        if (requested.Count == 0)
        {
            return Result.Success();
        }

        var conflicts = await roles.GetIncompatibilitiesAsync(
            [.. requested.Select(role => role.Id)], cancellationToken);

        var blocking = conflicts
            .Where(rule => rule.IsBlocking && rule.IsActive)
            .Where(rule => requested.Any(role => role.Id == rule.RoleId)
                           && requested.Any(role => role.Id == rule.ConflictingRoleId))
            .ToList();

        if (blocking.Count > 0)
        {
            return Result.Failure(Error.SegregationOfDuties(
                "Those roles cannot be held together: " +
                string.Join("; ", blocking.Select(rule => rule.Reason))));
        }

        var isFirst = true;
        foreach (var role in requested)
        {
            await roles.AddUserRoleAsync(new UserRole
            {
                TenantId = user.TenantId,
                BusinessUnitId = user.BusinessUnitId,
                UserId = user.Id,
                RoleId = role.Id,
                Status = UserRoleAssignmentStatus.Active,
                IsPrimary = isFirst,
                AssignedAtUtc = now,
                AssignedByUserId = currentUser.UserId,
                EffectiveFromUtc = now,
                Justification = "Assigned when the user was created."
            }, cancellationToken);

            isFirst = false;
        }

        return Result.Success();
    }

    private string BuildActivationUrl(string hostName, string token)
    {
        // Same localhost shortcut as CreateOrganisationCommand had, and the same fault: an
        // invited user was sent to the platform host, where their Organisation does not resolve.
        return _client.TenantUrl(hostName, _client.InvitationPath, token);
    }
}
