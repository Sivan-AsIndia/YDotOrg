using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Application.Features.Authentication.Mappings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Domain.ValueObjects;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Commands.AcceptInvitation;

/// <summary>IAM-AUTH-02. Redeems an invitation and activates the account.</summary>
public sealed record AcceptInvitationCommand(AcceptInvitationRequest Request);

/// <summary>What the activation screen shows before a password is typed.</summary>
public sealed record PreviewInvitationQuery(string Token);

/// <summary>Starts enrolling a second factor while activating an invited account.</summary>
public sealed record BeginInvitationMfaEnrolmentCommand(BeginInvitationMfaEnrolmentRequest Request);

/// <summary>Proves the factor enrolled during activation actually works.</summary>
public sealed record VerifyInvitationMfaEnrolmentCommand(VerifyInvitationMfaEnrolmentRequest Request);

/// <summary>Asks for a replacement invitation when the current link has lapsed.</summary>
public sealed record RequestNewInvitationCommand(RequestNewInvitationRequest Request);

/// <summary>Leaves the activation flow without completing it.</summary>
public sealed record CancelActivationCommand(CancelActivationRequest Request);

/// <summary>
/// Invitation acceptance.
///
/// THE RULE THIS HANDLER EXISTS TO ENFORCE. Section 9 of the brief:
///
///     "The invitation must never accidentally activate/create a user in another Tenant.
///      If the same email exists in another Tenant, that does NOT mean the user record
///      from that Tenant should be reused."
///
/// The mechanism is simple and worth stating plainly: this handler NEVER looks a user up by
/// e-mail. It resolves the invitation by its token hash, and then acts on
/// <c>invitation.UserId</c> — the one user, in the one Organisation, that this invitation was
/// created for. A global e-mail lookup is exactly how an invitation meant for TEN001 would
/// activate the unrelated john@gmail.com in TEN002, so the lookup simply does not exist.
///
/// A TENANTADMIN ACCEPTANCE ALSO MOVES THE ORGANISATION FORWARD. For an ordinary user,
/// accepting activates one person. For the first administrator of a new Organisation, it also
/// advances the Organisation lifecycle from Invited to InvitationAccepted to
/// ProfileIncomplete, which is what puts the onboarding wizard in front of them instead of an
/// empty dashboard.
/// </summary>
public sealed class AcceptInvitationCommandHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    IRoleRepository roles,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    IMfaChallengeService mfa,
    IMfaEnrolmentService enrolment,
    IOrganisationStructureRepository orgStructure,
    IGlobalMasterReadService globalMasters,
    ISessionTokenService sessions,
    IEffectiveAccessService effectiveAccess,
    INotificationService notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    IUserAgentParser userAgents,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    public async Task<Result<AcceptInvitationResponse>> HandleAsync(
        AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Failure<AcceptInvitationResponse>(
                Error.Validation("The passwords do not match.",
                    [new ValidationError(nameof(request.ConfirmPassword), "The passwords do not match.")]));
        }

        // ---- Resolve the invitation from its token, NOT the e-mail --------------------------
        var invitation = await invitations.GetByTokenHashAsync(
            tokenHasher.Hash(request.Token ?? string.Empty), cancellationToken);

        if (invitation is null)
        {
            return Result.Failure<AcceptInvitationResponse>(Error.InvitationInvalid());
        }

        if (invitation.Status == InvitationStatus.Accepted)
        {
            return Result.Failure<AcceptInvitationResponse>(Error.InvitationAlreadyAccepted());
        }

        if (!invitation.IsRedeemable(now))
        {
            return Result.Failure<AcceptInvitationResponse>(Error.InvitationExpired());
        }

        // ---- The user this invitation names. Never a lookup by address. -----------------------
        var user = await users.FindByIdInTenantAsync(invitation.UserId, invitation.TenantId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AcceptInvitationResponse>(Error.InvitationInvalid());
        }

        var businessUnit = await businessUnits.GetByIdAsync(invitation.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<AcceptInvitationResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenant = invitation.TenantId == Guid.Empty
            ? null
            : await tenants.GetByIdAsync(invitation.TenantId, cancellationToken);

        // ---- Password policy ----------------------------------------------------------------------
        var minimumLength = Math.Max(_security.PasswordMinimumLength, tenant?.PasswordMinimumLength ?? 0);
        var policyFailures = passwordHasher.ValidatePolicy(request.Password ?? string.Empty, minimumLength);

        if (policyFailures.Count > 0)
        {
            return Result.Failure<AcceptInvitationResponse>(
                Error.WeakPassword("That password does not meet the requirements.",
                    [.. policyFailures.Select(message => new ValidationError(nameof(request.Password), message))]));
        }

        // ---- Activate the account ---------------------------------------------------------------------
        user.PasswordHash = passwordHasher.Hash(request.Password!);
        user.PasswordChangedAtUtc = now;
        user.MustChangePassword = false;
        user.Status = UserStatus.Active;
        user.EmailConfirmed = true;
        user.EmailConfirmedAtUtc = now;

        // A new stamp invalidates anything minted before activation.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            user.FirstName = request.FirstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.LastName))
        {
            user.LastName = request.LastName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName) || !string.IsNullOrWhiteSpace(request.LastName))
        {
            user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            user.MobileCountryCode = request.MobileCountryCode?.Trim();
            user.MobileNumber = request.MobileNumber.Trim();
            user.PhoneNumber = user.ToE164();
        }

        if (user.AccessStartsAtUtc == default)
        {
            user.AccessStartsAtUtc = now;
        }

        // ---- Close the invitation -----------------------------------------------------------------------
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAtUtc = now;
        invitation.AcceptedFromIpAddress = currentUser.IpAddress;
        invitation.AcceptedUserAgent = currentUser.UserAgent;

        // ---- Grant the initial role -----------------------------------------------------------------------
        await AssignInitialRoleAsync(user, invitation, tenant, now, cancellationToken);

        // ---- A TenantAdmin acceptance moves the Organisation forward too ------------------------------------
        var requiresProfile = false;

        if (invitation.InvitationType == InvitationType.TenantAdmin && tenant is not null)
        {
            requiresProfile = AdvanceTenantOnboarding(tenant, user, now);

            await tenants.AddStatusHistoryAsync(new TenantStatusHistory
            {
                BusinessUnitId = tenant.BusinessUnitId,
                TenantId = tenant.Id,
                FromStatus = TenantStatus.Invited,
                ToStatus = tenant.Status,
                OccurredAtUtc = now,
                ActorUserId = user.Id,
                ActorDisplayName = user.DisplayName,
                Notes = "TenantAdmin accepted the invitation and activated their account.",
                CorrelationId = currentUser.CorrelationId
            }, cancellationToken);
        }

        await audit.WriteAnonymousAsync(
            AuditActionCodes.UserActivated, nameof(User), user.Id,
            invitation.BusinessUnitId, invitation.TenantId == Guid.Empty ? null : invitation.TenantId,
            AuditResult.Succeeded, user.DisplayName,
            new { invitation.InvitationType, invitation.Reference },
            cancellationToken);

        // ---- Sign them straight in -----------------------------------------------------------------------------
        //
        // Asking somebody to type the password they chose ten seconds ago is a pointless
        // hurdle, and the invitation token already proved they control the mailbox.
        var client = userAgents.Parse(currentUser.UserAgent, request.ClientType.ToString());
        var scope = user.IsSuperAdmin ? AccessScopeType.Global : AccessScopeType.Tenant;
        var access = await effectiveAccess.ResolveAsync(user, tenant?.Id, cancellationToken);

        var tokens = await sessions.IssueAsync(
            user, tenant, businessUnit, scope, request.ClientType,
            mfaCompleted: true, rememberMe: false, hostName: invitation.InvitationHostName,
            request.DeviceIdentifier, client.DeviceName, isTrustedDevice: false, cancellationToken);

        user.LastLoginAtUtc = now;
        user.LastSignedInAtUtc = now;
        user.LastLoginIpAddress = currentUser.IpAddress;
        user.LastLoginUserAgent = currentUser.UserAgent;
        user.LastLoginClientType = request.ClientType;
        user.LastLoginBrowser = client.Browser;
        user.LastLoginOperatingSystem = client.OperatingSystem;
        user.LastLoginDeviceIdentifier = request.DeviceIdentifier;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Delivery failure must not undo an activation that has already been committed.
        await notifications.SendWelcomeAsync(
            user, tenant, businessUnit, BuildSignInUrl(tenant, businessUnit), cancellationToken);

        var mfaRequired = user.IsMfaRequired(tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional);

        // ---- Backup codes -------------------------------------------------------------------
        //
        // Generated only when a second factor was actually enrolled during activation. Codes
        // that back up nothing are just one more secret for somebody to look after, and handing
        // them out unasked teaches people to ignore the warning that comes with them.
        //
        // THIS IS THE ONLY TIME THEY ARE READABLE. Only hashes are stored, so there is no second
        // chance to display them and no support process that can recover them.
        var recoveryCodes = user.MfaEnabled
            ? await mfa.GenerateRecoveryCodesAsync(user, cancellationToken)
            : [];

        if (recoveryCodes.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new AcceptInvitationResponse(
            Succeeded: true,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.RefreshToken,
            tokens.SessionId,
            AuthenticationMappingConfig.ToAuthenticatedUser(user, access),
            AuthenticationMappingConfig.ToTenantContext(tenant, businessUnit, scope, isTenantMode: false),
            requiresProfile,
            RequiresMfaEnrolment: mfaRequired && !user.MfaEnabled,
            Message: requiresProfile
                ? "Your account is active. Complete your organisation profile to continue."
                : "Your account is active.",
            RecoveryCodes: recoveryCodes,
            MfaEnrolled: user.MfaEnabled,
            RecoveryCodeNotice: recoveryCodes.Count > 0
                ? "Save these codes somewhere safe. Each one works once, and this is the only "
                  + "time they can be shown. If you lose them, generate a new set from your "
                  + "security page."
                : string.Empty));
    }

    /// <summary>
    /// The preview shown before a password is typed: which Organisation, and who it was for.
    /// Anonymous, so it says as little as it can while still letting somebody recognise their
    /// own invitation.
    /// </summary>
    public async Task<Result<InvitationPreviewResponse>> HandleAsync(
        PreviewInvitationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<InvitationPreviewResponse>(Error.Dependency("The platform is not configured."));
        }

        var invitation = await invitations.GetByTokenHashAsync(
            tokenHasher.Hash(query.Token ?? string.Empty), cancellationToken);

        if (invitation is null)
        {
            return Result.Success(
                AuthenticationMappingConfig.InvalidInvitation(businessUnit, _security));
        }

        var user = await users.FindByIdInTenantAsync(invitation.UserId, invitation.TenantId, cancellationToken);
        if (user is null)
        {
            return Result.Success(AuthenticationMappingConfig.InvalidInvitation(
                businessUnit, _security, invitation.InvitationType, invitation.ExpiresAtUtc));
        }

        var tenant = invitation.TenantId == Guid.Empty
            ? null
            : await tenants.GetByIdAsync(invitation.TenantId, cancellationToken);

        // The role, department and unit are resolved here so the screen can show somebody what
        // they are being given before they accept. An invitation to the wrong job is exactly the
        // thing a person should be able to spot on this screen rather than on their first Monday.
        var roleSummary = await DescribeInvitedRoleAsync(invitation, tenant, cancellationToken);

        var department = user.DepartmentId.HasValue
            ? await orgStructure.GetDepartmentAsync(user.DepartmentId.Value, cancellationToken)
            : null;

        var unit = user.OrganisationUnitId.HasValue
            ? await orgStructure.GetUnitAsync(user.OrganisationUnitId.Value, cancellationToken)
            : null;

        // The dialling prefixes for the mobile number on the SMS enrolment step, taken from the
        // country catalogue rather than a literal list, and sent WITH the preview because this
        // screen is anonymous: it has no session with which to call the lookup endpoint itself.
        // Countries are ITenantScoped, so the platform catalogue (TenantId null) is visible even
        // though no Organisation is resolved on an anonymous request.
        var dialingCodes = await GetDialingCodesAsync(cancellationToken);

        return Result.Success(AuthenticationMappingConfig.ToPreviewResponse(
            invitation, user, tenant, businessUnit, clock.UtcNow, _security,
            roleSummary, department?.Name, unit?.Name, dialingCodes));
    }

    /// <summary>
    /// Distinct dialling prefixes from the country catalogue, in country sort order.
    ///
    /// Never throws. A catalogue that cannot be read leaves the picker empty and the person
    /// types the number themselves — the same "degrade rather than break" rule the lookup
    /// service applies, and far better than failing the whole preview over a dropdown.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetDialingCodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var countries = await globalMasters.LookupCountriesAsync(cancellationToken);

            return countries
                .Select(country => country.PhoneCountryCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Names the role the invitation grants, falling back to the Organisation default when the
    /// invitation named none - which is the same fallback acceptance itself applies, so the
    /// screen never promises one role and delivers another.
    /// </summary>
    private async Task<string?> DescribeInvitedRoleAsync(
        UserInvitation invitation, Tenant? tenant, CancellationToken cancellationToken)
    {
        if (invitation.InitialRoleId.HasValue)
        {
            var named = await roles.GetByIdAsync(invitation.InitialRoleId.Value, cancellationToken);
            return named?.Name ?? named?.Code;
        }

        if (tenant is null)
        {
            return null;
        }

        var fallback = invitation.InvitationType == InvitationType.TenantAdmin
            ? await roles.GetByCodeAsync(RoleCodes.TenantAdmin, tenant.Id, cancellationToken)
            : await roles.GetDefaultRoleAsync(tenant.Id, cancellationToken);

        return fallback?.Name ?? fallback?.Code;
    }

    // =====================================================================================
    // Enrolling a second factor DURING activation
    //
    // These calls are authorised by the INVITATION TOKEN, because the person has no session yet
    // - that is the whole point of the screen they are on. The token identifies exactly one user
    // in exactly one Organisation, so it is a narrower credential than a session, not a looser one.
    // =====================================================================================

    /// <summary>
    /// Starts enrolling a second factor from the activation screen.
    ///
    /// The rules are the shared ones in <see cref="IMfaEnrolmentService"/>: the secret comes back
    /// once, the method is created Pending, and it does not count as a factor until a code from
    /// it has been verified. That last part matters more here than anywhere else, because the
    /// very next thing this person does is sign in with it.
    /// </summary>
    public async Task<Result<MfaEnrolmentResponse>> HandleAsync(
        BeginInvitationMfaEnrolmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = await ResolveActivationContextAsync(command.Request.Token, cancellationToken);
        if (context.Error is not null)
        {
            return Result.Failure<MfaEnrolmentResponse>(context.Error);
        }

        var (_, user, tenant, businessUnit) = context.Value;

        // A mobile number typed on the activation screen is stored before the SMS enrolment can
        // use it. Without this the enrolment refuses a number the person has just supplied,
        // which reads as the form ignoring them.
        if (command.Request.MethodType == MfaMethodType.Sms
            && !string.IsNullOrWhiteSpace(command.Request.MobileNumber))
        {
            var mobile = MobileNumberValue.TryParse(
                command.Request.MobileCountryCode, command.Request.MobileNumber);

            if (mobile is null)
            {
                return Result.Failure<MfaEnrolmentResponse>(
                    Error.Validation("That mobile number is not valid.",
                        [new ValidationError(nameof(command.Request.MobileNumber),
                            "Enter a valid mobile number.")]));
            }

            user.MobileCountryCode = mobile.CountryCode;
            user.MobileNumber = mobile.Number;
        }

        var result = await enrolment.BeginAsync(
            user, tenant, businessUnit, command.Request.MethodType,
            command.Request.Label, cancellationToken);

        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Confirms the factor enrolled during activation.
    ///
    /// Only after this does the account count as having a second factor, which is what makes the
    /// recovery codes handed out at the end of activation worth anything.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        VerifyInvitationMfaEnrolmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = await ResolveActivationContextAsync(command.Request.Token, cancellationToken);
        if (context.Error is not null)
        {
            return Result.Failure<OutcomeResponse>(context.Error);
        }

        var (_, user, _, _) = context.Value;

        var confirmed = await enrolment.ConfirmAsync(
            user, command.Request.MethodId, command.Request.Code, cancellationToken);

        // Saved either way: the audit row recording a bad attempt is written inside ConfirmAsync,
        // and an unrecorded failed attempt is the one nobody can investigate afterwards.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (confirmed.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(confirmed.Error!);
        }

        var method = confirmed.Value!;

        return Result.Success(new OutcomeResponse(
            method.Id,
            method.Status.ToString(),
            method.Version,
            "Verification method confirmed. Finish activating your account to receive your recovery codes.",
            ["Activate"]));
    }

    /// <summary>
    /// Sends a replacement invitation and RETIRES THE CURRENT ONE.
    ///
    /// Both halves matter. Issuing a new link while the old one still works would mean a link
    /// somebody reported as compromised keeps opening the door.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RequestNewInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var invitation = await invitations.GetByTokenHashAsync(
            tokenHasher.Hash(command.Request.Token ?? string.Empty), cancellationToken);

        var user = invitation is null
            ? null
            : await users.FindByIdInTenantAsync(invitation.UserId, invitation.TenantId, cancellationToken);

        var businessUnit = invitation is null
            ? null
            : await businessUnits.GetByIdAsync(invitation.BusinessUnitId, cancellationToken);

        // An unknown or spent token is answered exactly like a good one. Anything else turns this
        // endpoint into a way of testing whether an invitation token is real.
        if (invitation is null
            || invitation.Status == InvitationStatus.Accepted
            || user is null
            || businessUnit is null)
        {
            return Result.Success(new OutcomeResponse(
                Guid.Empty, "Sent", 0,
                "If that invitation is still open, a new link is on its way.",
                []));
        }

        var tenant = invitation.TenantId == Guid.Empty
            ? null
            : await tenants.GetByIdAsync(invitation.TenantId, cancellationToken);

        var replacementToken = tokenHasher.GenerateToken();

        invitation.TokenHash = tokenHasher.Hash(replacementToken);
        invitation.ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays);
        invitation.LastSentAtUtc = now;
        invitation.ResendCount += 1;
        invitation.Status = InvitationStatus.Resent;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.InvitationResent, nameof(UserInvitation), invitation.Id,
            invitation.BusinessUnitId, invitation.TenantId == Guid.Empty ? null : invitation.TenantId,
            AuditResult.Succeeded, user.DisplayName,
            new { invitation.Reference, RequestedByRecipient = true },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The link is rebuilt against the Organisation host the invitation was created for, so
        // following it resolves the right Tenant. Sending a platform-host link would land the
        // person on the wrong sign-in page after activating.
        await notifications.SendInvitationAsync(
            user, invitation, tenant, businessUnit,
            BuildActivationUrl(invitation, tenant, businessUnit, replacementToken),
            cancellationToken);

        return Result.Success(new OutcomeResponse(
            invitation.Id, invitation.Status.ToString(), invitation.Version,
            "If that invitation is still open, a new link is on its way.",
            []));
    }

    /// <summary>
    /// Leaves the activation flow without completing it.
    ///
    /// THE INVITATION STAYS USABLE, deliberately. Somebody who backs out to check a detail with
    /// their administrator should be able to return to the same link; burning it here would turn
    /// an ordinary moment of hesitation into a support call. What this does record is that they
    /// reached the screen and stopped, which is worth knowing when an invitation is never taken up.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelActivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invitation = await invitations.GetByTokenHashAsync(
            tokenHasher.Hash(command.Request.Token ?? string.Empty), cancellationToken);

        if (invitation is not null && invitation.Status != InvitationStatus.Accepted)
        {
            var user = await users.FindByIdInTenantAsync(invitation.UserId, invitation.TenantId, cancellationToken);

            await audit.WriteAnonymousAsync(
                AuditActionCodes.InvitationActivationAbandoned, nameof(UserInvitation), invitation.Id,
                invitation.BusinessUnitId, invitation.TenantId == Guid.Empty ? null : invitation.TenantId,
                AuditResult.Succeeded, user?.DisplayName,
                new { invitation.Reference, command.Request.Reason },
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new OutcomeResponse(
            invitation?.Id ?? Guid.Empty, "Cancelled", invitation?.Version ?? 0,
            "You can return to your invitation link whenever you are ready.",
            []));
    }

    /// <summary>
    /// Resolves the invitation, the user, the Organisation and the BusinessUnit from a token.
    ///
    /// Shared by the two enrolment calls above, which are otherwise four identical lookups each
    /// with four identical ways to get the failure handling subtly wrong.
    /// </summary>
    private async Task<ActivationContext> ResolveActivationContextAsync(
        string? token, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByTokenHashAsync(
            tokenHasher.Hash(token ?? string.Empty), cancellationToken);

        if (invitation is null)
        {
            return new ActivationContext(Error.InvitationInvalid());
        }

        if (invitation.Status == InvitationStatus.Accepted)
        {
            return new ActivationContext(Error.InvitationAlreadyAccepted());
        }

        if (!invitation.IsRedeemable(clock.UtcNow))
        {
            return new ActivationContext(Error.InvitationExpired());
        }

        var user = await users.FindByIdInTenantAsync(invitation.UserId, invitation.TenantId, cancellationToken);
        if (user is null)
        {
            return new ActivationContext(Error.InvitationInvalid());
        }

        var businessUnit = await businessUnits.GetByIdAsync(invitation.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return new ActivationContext(Error.Dependency("The platform is not configured."));
        }

        var tenant = invitation.TenantId == Guid.Empty
            ? null
            : await tenants.GetByIdAsync(invitation.TenantId, cancellationToken);

        return new ActivationContext(invitation, user, tenant, businessUnit);
    }

    /// <summary>Either the four things an activation call needs, or the reason it cannot have them.</summary>
    private readonly record struct ActivationContext
    {
        public ActivationContext(Error error)
        {
            Error = error;
            Value = default;
        }

        public ActivationContext(
            UserInvitation invitation, User user, Tenant? tenant, BusinessUnit businessUnit)
        {
            Error = null;
            Value = (invitation, user, tenant, businessUnit);
        }

        public Error? Error { get; }

        public (UserInvitation Invitation, User User, Tenant? Tenant, BusinessUnit BusinessUnit) Value { get; }
    }

    /// <summary>
    /// Grants the role the invitation named, or the Organisation default when it named none.
    /// Without this a freshly activated user signs in successfully and can then see nothing,
    /// which reads as a broken account rather than a missing role.
    /// </summary>
    private async Task AssignInitialRoleAsync(
        User user, UserInvitation invitation, Tenant? tenant, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await roles.GetUserRolesInTenantAsync(user.Id, user.TenantId, cancellationToken);
        if (existing.Any(assignment => assignment.IsEffective(now)))
        {
            return;
        }

        var roleId = invitation.InitialRoleId;

        if (roleId is null && tenant is not null)
        {
            var fallback = invitation.InvitationType == InvitationType.TenantAdmin
                ? await roles.GetByCodeAsync(RoleCodes.TenantAdmin, tenant.Id, cancellationToken)
                : await roles.GetDefaultRoleAsync(tenant.Id, cancellationToken);

            roleId = fallback?.Id;
        }

        if (roleId is null)
        {
            return;
        }

        await roles.AddUserRoleAsync(new UserRole
        {
            TenantId = user.TenantId,
            BusinessUnitId = user.BusinessUnitId,
            UserId = user.Id,
            RoleId = roleId.Value,
            Status = UserRoleAssignmentStatus.Active,
            IsPrimary = true,
            AssignedAtUtc = now,
            AssignedByUserId = invitation.InvitedByUserId,
            EffectiveFromUtc = now,
            Justification = "Granted on invitation acceptance."
        }, cancellationToken);
    }

    /// <summary>
    /// Moves the Organisation along when its first administrator activates.
    ///
    /// Invited becomes ProfileIncomplete in one step, through the legal
    /// InvitationAccepted rung. Returns true when the profile still needs completing, which
    /// is what routes the person to the onboarding wizard.
    /// </summary>
    private static bool AdvanceTenantOnboarding(Tenant tenant, User user, DateTimeOffset now)
    {
        if (tenant.Status != TenantStatus.Invited)
        {
            // Already further along — a second administrator activating, or a resend after
            // the profile was started. Nothing to advance, but the wizard is still due if the
            // profile is not finished.
            return tenant.IsProfileEditable;
        }

        tenant.Status = TenantStatus.ProfileIncomplete;
        tenant.InvitationAcceptedAtUtc = now;

        return true;
    }

    /// <summary>
    /// The activation link for a replacement invitation.
    ///
    /// Built against the host recorded on the invitation where there is one, and against the
    /// Organisation subdomain otherwise. Either way it must NOT be the platform host: following
    /// a platform link would resolve the wrong Tenant, and the token would then be refused for
    /// an Organisation mismatch that the person has no way to understand or fix.
    /// </summary>
    private string BuildActivationUrl(
        UserInvitation invitation, Tenant? tenant, BusinessUnit businessUnit, string token)
    {
        var host = !string.IsNullOrWhiteSpace(invitation.InvitationHostName)
            ? invitation.InvitationHostName
            : tenant is not null
                ? $"{tenant.Subdomain}.{businessUnit.RootDomain}"
                : null;

        return _client.TenantUrl(host, _client.InvitationPath, token);
    }

    /// <summary>
    /// The sign-in link for the welcome e-mail, pointed at the Organisation own host so the
    /// right Tenant is resolved when the person follows it.
    /// </summary>
    private string BuildSignInUrl(Tenant? tenant, BusinessUnit businessUnit)
    {
        var host = tenant is null ? null : $"{tenant.Subdomain}.{businessUnit.RootDomain}";

        return _client.TenantUrl(host, _client.SignInPath);
    }
}
