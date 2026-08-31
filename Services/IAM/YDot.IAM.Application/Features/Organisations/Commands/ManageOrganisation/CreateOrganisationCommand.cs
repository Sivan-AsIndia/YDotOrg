using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;

/// <summary>SuperAdmin creating an Organisation and inviting its first administrator.</summary>
public sealed record CreateOrganisationCommand(CreateOrganisationRequest Request);

/// <summary>Checks a subdomain before the create form is submitted.</summary>
public sealed record CheckSubdomainQuery(CheckSubdomainRequest Request);

/// <summary>Re-sends the TenantAdmin invitation.</summary>
public sealed record ResendOrganisationInvitationCommand(Guid TenantId);

/// <summary>
/// Organisation creation.
///
/// THIS IS THE ONE PLACE A TENANT COMES INTO EXISTENCE, and it does five things in a single
/// transaction because an Organisation missing any of them is not usable:
///
/// <code>
/// 1. Tenant                the Organisation itself, status Invited
/// 2. TenantDomain          ten1.ngoplanet.com, so the Organisation can be reached at all
/// 3. Roles                 the standard set, copied per Organisation
/// 4. TenantAdmin user      status Invited, no password yet
/// 5. UserInvitation        the activation link, e-mailed
/// </code>
///
/// Splitting these across endpoints would allow an Organisation with no host, or with no
/// administrator — states nothing downstream knows how to handle, and which somebody would
/// then have to repair by hand.
///
/// WHY THE ROLES ARE COPIED RATHER THAN SHARED. Roles are Tenant-specific, so every
/// Organisation gets its own rows. Two Organisations both having ADMIN is expected and they
/// have nothing to do with each other. Sharing one row would mean an Organisation editing its
/// administrator role changed everybody else administrator too.
/// </summary>
public sealed class CreateOrganisationCommandHandler(
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IUserRepository users,
    IRoleRepository roles,
    IPermissionRepository permissions,
    IInvitationRepository invitations,
    IMenuRepository menus,
    ITokenHasher tokenHasher,
    INotificationService notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<EmailSettings> emailOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly EmailSettings _email = emailOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    public async Task<Result<CreateOrganisationResponse>> HandleAsync(
        CreateOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<CreateOrganisationResponse>(
                Error.Dependency("The platform is not configured."));
        }

        // ---- The subdomain, validated hard ----------------------------------------------
        //
        // This is the string that resolves an anonymous sign-in to an Organisation, so a bad
        // one is a security problem rather than a cosmetic one.
        var subdomain = SubdomainValue.TryParse(request.Subdomain);
        if (subdomain is null)
        {
            return Result.Failure<CreateOrganisationResponse>(
                SubdomainValue.IsReserved(request.Subdomain)
                    ? Error.SubdomainReserved()
                    : Error.Validation("That web address is not valid.",
                        [new ValidationError(nameof(request.Subdomain),
                            "Use lower-case letters, digits and hyphens only.")]));
        }

        if (await tenants.SubdomainExistsAsync(subdomain.Value, businessUnit.Id, null, cancellationToken))
        {
            return Result.Failure<CreateOrganisationResponse>(Error.SubdomainUnavailable());
        }

        var hostName = subdomain.ToHostName(businessUnit.RootDomain);

        if (await tenants.HostNameExistsAsync(hostName, null, cancellationToken))
        {
            return Result.Failure<CreateOrganisationResponse>(Error.SubdomainUnavailable());
        }

        // ---- Ceiling on Organisations -----------------------------------------------------------
        if (businessUnit.MaximumTenants.HasValue)
        {
            var existing = await tenants.CountAsync(businessUnit.Id, cancellationToken);
            if (existing >= businessUnit.MaximumTenants.Value)
            {
                return Result.Failure<CreateOrganisationResponse>(Error.TenantLimitReached());
            }
        }

        // ---- The Organisation code --------------------------------------------------------------
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await tenants.NextTenantCodeAsync(businessUnit.Id, cancellationToken)
            : CodeValue.FromName(request.Code);

        if (await tenants.CodeExistsAsync(code, businessUnit.Id, null, cancellationToken))
        {
            return Result.Failure<CreateOrganisationResponse>(
                Error.Duplicate($"An organisation with code {code} already exists."));
        }

        var adminEmail = EmailValue.TryParse(request.AdminEmail);
        if (adminEmail is null)
        {
            return Result.Failure<CreateOrganisationResponse>(
                Error.Validation("Enter a valid administrator e-mail address.",
                    [new ValidationError(nameof(request.AdminEmail), "That e-mail address is not valid.")]));
        }

        // ---- 1. The Organisation ---------------------------------------------------------------------
        var tenant = request.ToEntity(businessUnit, code, subdomain.Value, now);
        await tenants.AddAsync(tenant, cancellationToken);

        // ---- 2. Its primary host --------------------------------------------------------------------
        //
        // Verified on creation, because the platform already controls the apex domain. A
        // custom domain added later starts unverified and needs a DNS record.
        await tenants.AddDomainAsync(new TenantDomain
        {
            BusinessUnitId = businessUnit.Id,
            TenantId = tenant.Id,
            HostName = hostName,
            DomainType = TenantDomainType.Subdomain,
            IsPrimary = true,
            IsVerified = true,
            VerifiedAtUtc = now,
            VerifiedByUserId = currentUser.UserId,
            IsActive = true
        }, cancellationToken);

        // ---- 3. The Organisation own roles ------------------------------------------------------------
        var tenantAdminRole = await SeedTenantRolesAsync(tenant, businessUnit, now, cancellationToken);

        // ---- 4. The TenantAdmin user ---------------------------------------------------------------------
        //
        // Created with NO password. The account genuinely cannot be signed into until the
        // invitation is accepted, rather than relying on a status check alone.
        var username = string.IsNullOrWhiteSpace(request.AdminUsername)
            ? adminEmail.LocalPart
            : request.AdminUsername.Trim().ToLowerInvariant();

        var adminUser = new User
        {
            TenantId = tenant.Id,
            BusinessUnitId = businessUnit.Id,
            Code = await users.NextUserCodeAsync(tenant.Id, cancellationToken),
            FirstName = request.AdminFirstName.Trim(),
            LastName = request.AdminLastName.Trim(),
            DisplayName = $"{request.AdminFirstName.Trim()} {request.AdminLastName.Trim()}".Trim(),
            Email = adminEmail.Value,
            NormalizedEmail = adminEmail.Value.ToUpperInvariant(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            EmailConfirmed = false,
            Status = UserStatus.Invited,
            AccountCategory = UserAccountCategory.Employee,
            PrivilegeLevel = PrivilegeLevel.TenantAdmin,
            IsTenantAdmin = true,
            IsSuperAdmin = false,
            MfaRequirement = MfaRequirement.Inherited,
            AccessStartsAtUtc = now,
            CredentialSetupMethod = CredentialSetupMethod.InvitationLink,
            LockoutEnabled = true,
            MobileCountryCode = request.ContactPhoneCountryCode?.Trim(),
            MobileNumber = request.ContactPhone?.Trim()
        };

        adminUser.PhoneNumber = adminUser.ToE164();

        await users.AddAsync(adminUser, cancellationToken);

        // Give them the TenantAdmin role straight away, so activation lands them somewhere
        // useful rather than on an empty dashboard.
        if (tenantAdminRole is not null)
        {
            await roles.AddUserRoleAsync(new UserRole
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                UserId = adminUser.Id,
                RoleId = tenantAdminRole.Id,
                Status = UserRoleAssignmentStatus.Active,
                IsPrimary = true,
                AssignedAtUtc = now,
                AssignedByUserId = currentUser.UserId,
                EffectiveFromUtc = now,
                Justification = "First administrator of the organisation."
            }, cancellationToken);
        }

        // ---- 5. The invitation ----------------------------------------------------------------------------
        var plaintextToken = tokenHasher.GenerateToken();
        var invitation = new UserInvitation
        {
            TenantId = tenant.Id,
            BusinessUnitId = businessUnit.Id,
            UserId = adminUser.Id,
            Email = adminEmail.Value,
            NormalizedEmail = adminEmail.Value.ToUpperInvariant(),
            InvitationType = InvitationType.TenantAdmin,
            InitialRoleId = tenantAdminRole?.Id,
            TokenHash = tokenHasher.Hash(plaintextToken),
            Reference = tokenHasher.GenerateReference("INV"),
            ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays),
            Status = InvitationStatus.Pending,
            InvitedByUserId = currentUser.UserId,
            InvitedAtUtc = now,
            // Captured now rather than rebuilt at send time, so a later change to the primary
            // domain cannot silently redirect an outstanding invitation.
            InvitationHostName = hostName,
            Message = request.InvitationMessage,
            LastSentAtUtc = request.SendInvitation ? now : null
        };

        await invitations.AddAsync(invitation, cancellationToken);

        // ---- Switch on the default menu for this Organisation -------------------------------------------------
        await SeedTenantMenusAsync(tenant, businessUnit, cancellationToken);

        // ---- Lifecycle row ------------------------------------------------------------------------------------
        await tenants.AddStatusHistoryAsync(new TenantStatusHistory
        {
            BusinessUnitId = businessUnit.Id,
            TenantId = tenant.Id,
            FromStatus = null,
            ToStatus = TenantStatus.Invited,
            OccurredAtUtc = now,
            ActorUserId = currentUser.UserId,
            ActorDisplayName = currentUser.DisplayName,
            Notes = $"Organisation created and administrator {adminEmail.Value} invited.",
            CorrelationId = currentUser.CorrelationId
        }, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantCreated, nameof(Tenant), tenant.Id, tenant.Name,
            new { tenant.Code, tenant.Subdomain, HostName = hostName, AdminEmail = adminEmail.Value },
            cancellationToken: cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantAdminInvited, nameof(User), adminUser.Id, adminUser.DisplayName,
            new { TenantId = tenant.Id, invitation.Reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // ---- Send it ----------------------------------------------------------------------------------------------
        //
        // AFTER the commit, and never inside it. A mail relay hiccup must not roll back an
        // Organisation that has already been created; the invitation can simply be re-sent.
        var activationUrl = BuildActivationUrl(hostName, plaintextToken);

        if (request.SendInvitation)
        {
            await notifications.SendInvitationAsync(
                adminUser, invitation, tenant, businessUnit, activationUrl, cancellationToken);
        }

        return Result.Success(new CreateOrganisationResponse(
            tenant.Id,
            tenant.Code,
            tenant.Name,
            tenant.Subdomain,
            hostName,
            tenant.Status,
            adminUser.Id,
            adminUser.Email!,

            // ASKED FOR *AND* ACTUALLY SENT. This reported request.SendInvitation alone, so with
            // the relay off the response said an invitation had been e-mailed when
            // EmailNotificationService had done nothing but write a log line. The screen then drew
            // its green "an invitation has been e-mailed to..." banner, and somebody went looking
            // in a mailbox for a message that was never sent.
            request.SendInvitation && _email.Enabled,

            invitation.ExpiresAtUtc,
            // Returned ONLY when the relay is off, so a developer can still walk the flow.
            // Never when e-mail is enabled: a live activation link in an API response is a
            // live activation link in a log file.
            _email.Enabled ? null : activationUrl,
            tenant.Version));
    }

    /// <summary>
    /// Checks a subdomain before the form is submitted.
    ///
    /// Answers only "available or not" and never lists what is taken, so it cannot be walked
    /// to enumerate the platform customers.
    /// </summary>
    public async Task<Result<CheckSubdomainResponse>> HandleAsync(
        CheckSubdomainQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<CheckSubdomainResponse>(Error.Dependency("The platform is not configured."));
        }

        var candidate = (query.Request.Subdomain ?? string.Empty).Trim().ToLowerInvariant();
        var parsed = SubdomainValue.TryParse(candidate);

        if (parsed is null)
        {
            var reserved = SubdomainValue.IsReserved(candidate);

            return Result.Success(new CheckSubdomainResponse(
                candidate,
                IsAvailable: false,
                reserved,
                IsValidFormat: !reserved,
                HostName: null,
                reserved
                    ? "That address is reserved by the platform. Choose another."
                    : "Use 1 to 63 lower-case letters, digits or hyphens, not starting or ending with a hyphen.",
                Suggestions: []));
        }

        var taken = await tenants.SubdomainExistsAsync(parsed.Value, businessUnit.Id, null, cancellationToken);
        var hostName = parsed.ToHostName(businessUnit.RootDomain);

        if (!taken)
        {
            return Result.Success(new CheckSubdomainResponse(
                parsed.Value, IsAvailable: true, IsReserved: false, IsValidFormat: true,
                hostName, "That address is available.", Suggestions: []));
        }

        // A handful of suggestions, checked for availability so none of them is also taken.
        var suggestions = new List<string>();
        for (var suffix = 1; suffix <= 12 && suggestions.Count < 3; suffix++)
        {
            var candidateSuffix = $"{parsed.Value}{suffix}";

            if (SubdomainValue.TryParse(candidateSuffix) is not null
                && !await tenants.SubdomainExistsAsync(candidateSuffix, businessUnit.Id, null, cancellationToken))
            {
                suggestions.Add(candidateSuffix);
            }
        }

        return Result.Success(new CheckSubdomainResponse(
            parsed.Value, IsAvailable: false, IsReserved: false, IsValidFormat: true,
            hostName, "That address is already taken.", suggestions));
    }

    /// <summary>
    /// Re-sends the TenantAdmin invitation, minting a fresh token.
    ///
    /// The old token stops working. Otherwise an invitation forwarded to the wrong person
    /// months ago would still be live alongside the new one.
    /// </summary>
    public async Task<Result<CreateOrganisationResponse>> HandleAsync(
        ResendOrganisationInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<CreateOrganisationResponse>(Error.TenantNotFound());
        }

        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<CreateOrganisationResponse>(Error.Dependency("The platform is not configured."));
        }

        var primaryDomain = await tenants.GetPrimaryDomainAsync(tenant.Id, cancellationToken);
        var hostName = primaryDomain?.HostName ?? $"{tenant.Subdomain}.{businessUnit.RootDomain}";

        var pending = await invitations.GetPendingForTenantAsync(tenant.Id, cancellationToken);
        var adminInvitation = pending.FirstOrDefault(item => item.InvitationType == InvitationType.TenantAdmin);

        if (adminInvitation is null)
        {
            return Result.Failure<CreateOrganisationResponse>(
                Error.NotFound("There is no outstanding administrator invitation for this organisation."));
        }

        // NAMES THE ORGANISATION EXPLICITLY, and must. This runs for a SuperAdmin on the platform
        // host, where no Organisation is selected - so the ambient filter reads "TenantId == null"
        // and GetByIdAsync cannot see the invited administrator at all. It reported "the invited
        // administrator no longer exists" for a user who was sitting right there on the invitation.
        // The Organisation comes from the invitation row already loaded above, not from the caller.
        var adminUser = await users.FindByIdInTenantAsync(
            adminInvitation.UserId, adminInvitation.TenantId, cancellationToken);
        if (adminUser is null)
        {
            return Result.Failure<CreateOrganisationResponse>(
                Error.NotFound("The invited administrator no longer exists."));
        }

        if (adminInvitation.Status == InvitationStatus.Accepted)
        {
            return Result.Failure<CreateOrganisationResponse>(Error.InvitationAlreadyAccepted());
        }

        var plaintextToken = tokenHasher.GenerateToken();
        adminInvitation.TokenHash = tokenHasher.Hash(plaintextToken);
        adminInvitation.ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays);
        adminInvitation.Status = InvitationStatus.Resent;
        adminInvitation.ResendCount += 1;
        adminInvitation.LastSentAtUtc = now;
        adminInvitation.InvitationHostName = hostName;

        await audit.WriteAsync(
            AuditActionCodes.UserInvitationResent, nameof(UserInvitation), adminInvitation.Id,
            adminUser.DisplayName,
            new { TenantId = tenant.Id, adminInvitation.ResendCount },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var activationUrl = BuildActivationUrl(hostName, plaintextToken);

        await notifications.SendInvitationAsync(
            adminUser, adminInvitation, tenant, businessUnit, activationUrl, cancellationToken);

        return Result.Success(new CreateOrganisationResponse(
            tenant.Id, tenant.Code, tenant.Name, tenant.Subdomain, hostName, tenant.Status,
            adminUser.Id, adminUser.Email!, InvitationSent: true, adminInvitation.ExpiresAtUtc,
            _email.Enabled ? null : activationUrl, tenant.Version));
    }

    /// <summary>
    /// Creates the standard role set inside a new Organisation and attaches their permissions.
    ///
    /// TenantAdmin is given <c>GrantsAllTenantPermissions</c> rather than an enumerated list,
    /// which is why adding a module later does not require every existing customer to re-map
    /// their administrator. The platform-only codes are excluded from every Tenant role, so no
    /// role edit can hand an Organisation the ability to create or approve Organisations.
    /// </summary>
    private async Task<Role?> SeedTenantRolesAsync(
        Tenant tenant, BusinessUnit businessUnit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var assignable = await permissions.GetTenantAssignableAsync(cancellationToken);
        var byCode = assignable.ToDictionary(permission => permission.Code, StringComparer.Ordinal);

        Role? tenantAdminRole = null;

        foreach (var definition in TenantRoleDefinitions.All)
        {
            var role = new Role
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                Code = definition.Code,
                NormalizedCode = definition.Code.ToUpperInvariant(),
                Name = definition.Name,
                NormalizedName = definition.Name.ToUpperInvariant(),
                Description = definition.Description,
                RoleType = RoleType.Tenant,
                Status = RoleStatus.Active,
                IsSystemRole = true,
                IsDefaultRole = definition.IsDefault,
                GrantsAllTenantPermissions = definition.GrantsAll,
                IsPrivileged = definition.IsPrivileged,
                Priority = definition.Priority
            };

            await roles.AddAsync(role, cancellationToken);

            if (definition.Code == RoleCodes.TenantAdmin)
            {
                tenantAdminRole = role;
            }

            // A role that grants everything needs no rows: the flag is the grant, and listing
            // eighty permissions would only go stale the moment one is added.
            if (definition.GrantsAll)
            {
                continue;
            }

            foreach (var permissionCode in definition.PermissionCodes)
            {
                if (!byCode.TryGetValue(permissionCode, out var permission))
                {
                    continue;
                }

                await roles.AddRolePermissionAsync(new RolePermission
                {
                    TenantId = tenant.Id,
                    BusinessUnitId = businessUnit.Id,
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    PermissionCode = permission.Code,
                    GrantedAtUtc = now,
                    GrantedByUserId = currentUser.UserId
                }, cancellationToken);
            }
        }

        return tenantAdminRole;
    }

    /// <summary>
    /// Switches on the default navigation for a new Organisation.
    ///
    /// Only the nodes marked <c>IsEnabledByDefault</c>, and never the platform-only branch —
    /// Organisation administration belongs to SuperAdmin and must not appear in a
    /// TenantAdmin sidebar however their roles are later configured.
    /// </summary>
    private async Task SeedTenantMenusAsync(
        Tenant tenant, BusinessUnit businessUnit, CancellationToken cancellationToken)
    {
        var catalogue = await menus.GetCatalogueAsync(cancellationToken);

        foreach (var definition in catalogue.Where(node => !node.IsPlatformOnly && node.IsEnabledByDefault))
        {
            await menus.AddTenantMenuAsync(new TenantMenu
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                MenuDefinitionId = definition.Id,
                IsEnabled = true,
                Status = MenuStatus.Active,
                IsSystemGenerated = true
            }, cancellationToken);
        }
    }

    /// <summary>
    /// The activation link, pointed at the Organisation own host so following it resolves the
    /// right Tenant when the person arrives.
    /// </summary>
    private string BuildActivationUrl(string hostName, string token)
    {
        // NO LOCALHOST SHORTCUT. There used to be one here, on the assumption that a subdomain
        // cannot be reached in development — but *.localhost resolves to loopback by RFC 6761
        // without a hosts entry, so ten1.localhost works exactly as ten1.ngoplanet.com does.
        // The shortcut sent the new administrator to the PLATFORM host, where their brand-new
        // Organisation does not resolve, and the token was then refused for a mismatch.
        return _client.TenantUrl(hostName, _client.InvitationPath, token);
    }
}
