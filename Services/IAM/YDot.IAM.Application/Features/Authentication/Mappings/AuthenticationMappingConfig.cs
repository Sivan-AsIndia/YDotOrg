using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Authentication.Mappings;

/// <summary>
/// Manual mapping for the Authentication slice. Plain static methods rather than a mapping
/// library: the rules are visible, they are debuggable, and nothing is discovered at run time.
///
/// The four <c>To…Response</c> factories below are the four branches a sign-in can take. They
/// exist as separate named methods rather than one builder with a pile of optional arguments,
/// because at a call site "return ToMfaPendingResponse(...)" says what happened and a
/// half-populated constructor does not.
/// </summary>
public static class AuthenticationMappingConfig
{
    /// <summary>Signed in, tokens issued, go to the dashboard.</summary>
    public static SignInResponse ToSuccessResponse(
        TokenResponse tokens,
        AuthenticatedUserResponse user,
        TenantContextResponse tenant) =>
        new(
            SignInResultStatus.Succeeded,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.TokenType,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            tokens.SessionId,
            user,
            tenant,
            ChallengeToken: null,
            MfaMaskedDestination: null,
            MfaMethodType: null,
            SelectableTenants: [],
            PasswordResetToken: null,
            AttemptsRemaining: null,
            LockoutMinutesRemaining: null,
            Message: "Signed in.");

    /// <summary>
    /// Password accepted, second factor outstanding.
    ///
    /// No tokens are returned. The client gets an opaque challenge token and nothing else,
    /// so a half-authenticated state cannot be mistaken for a full one.
    /// </summary>
    public static SignInResponse ToMfaPendingResponse(MfaChallengeResponse challenge) =>
        new(
            SignInResultStatus.MfaRequired,
            AccessToken: null,
            AccessTokenExpiresAtUtc: null,
            ExpiresInSeconds: 0,
            TokenType: "Bearer",
            RefreshToken: null,
            RefreshTokenExpiresAtUtc: null,
            SessionId: null,
            User: null,
            Tenant: null,
            challenge.ChallengeToken,
            challenge.MaskedDestination,
            challenge.MethodType,
            SelectableTenants: [],
            PasswordResetToken: null,
            AttemptsRemaining: challenge.AttemptsRemaining,
            LockoutMinutesRemaining: null,
            Message: "Enter the verification code to continue.");

    /// <summary>
    /// SuperAdmin authenticated but has not chosen an Organisation.
    ///
    /// A real token IS issued here, but it carries Global scope with no tenant_id and no
    /// Tenant permissions, so the only thing it can do is list Organisations and select one.
    /// </summary>
    public static SignInResponse ToTenantSelectionResponse(
        TokenResponse tokens,
        AuthenticatedUserResponse user,
        IReadOnlyList<Tenant> selectable) =>
        new(
            SignInResultStatus.TenantSelectionRequired,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.TokenType,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            tokens.SessionId,
            user,
            Tenant: null,
            ChallengeToken: null,
            MfaMaskedDestination: null,
            MfaMethodType: null,
            SelectableTenants: [.. selectable.Select(ToTenantOption)],
            PasswordResetToken: null,
            AttemptsRemaining: null,
            LockoutMinutesRemaining: null,
            Message: "Select an organisation to continue.");

    /// <summary>Signed in on a temporary password, and blocked until it is changed.</summary>
    public static SignInResponse ToPasswordChangeRequiredResponse(
        TokenResponse tokens,
        AuthenticatedUserResponse user,
        TenantContextResponse tenant) =>
        new(
            SignInResultStatus.PasswordChangeRequired,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.ExpiresInSeconds,
            tokens.TokenType,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            tokens.SessionId,
            user,
            tenant,
            ChallengeToken: null,
            MfaMaskedDestination: null,
            MfaMethodType: null,
            SelectableTenants: [],
            PasswordResetToken: null,
            AttemptsRemaining: null,
            LockoutMinutesRemaining: null,
            Message: "You must change your password before continuing.");

    public static TenantOptionResponse ToTenantOption(this Tenant tenant) =>
        new(
            tenant.Id,
            tenant.Code,
            tenant.Name,
            tenant.Subdomain,
            tenant.Status,
            tenant.LogoUrl,
            tenant.IsOperable);

    /// <summary>The compact user block embedded in every authentication response.</summary>
    public static AuthenticatedUserResponse ToAuthenticatedUser(
        this User user, EffectiveAccess access) =>
        new(
            user.Id,
            user.Code,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.UserName ?? string.Empty,
            user.AvatarUrl,
            user.Status,
            user.PrivilegeLevel,
            user.IsSuperAdmin,
            user.IsTenantAdmin,
            user.MfaEnabled,
            user.MustChangePassword,
            user.LastLoginAtUtc,
            user.PreferredCulture,
            user.TimeZone,
            [.. access.Roles.Select(role => role.Code)],
            [.. access.PermissionCodes.OrderBy(code => code, StringComparer.Ordinal)]);

    /// <summary>The Organisation context block embedded in every authentication response.</summary>
    public static TenantContextResponse ToTenantContext(
        Tenant? tenant,
        BusinessUnit businessUnit,
        AccessScopeType scope,
        bool isTenantMode) =>
        new(
            tenant?.Id,
            tenant?.Code,
            tenant?.Name,
            tenant?.Subdomain,
            tenant?.Status,
            businessUnit.Id,
            businessUnit.Code,
            businessUnit.Name,
            scope,
            isTenantMode,
            tenant?.LogoUrl ?? businessUnit.LogoUrl,
            tenant?.TimeZone ?? businessUnit.TimeZone,
            tenant?.DefaultCurrency ?? businessUnit.DefaultCurrency,
            tenant?.DefaultCulture ?? businessUnit.DefaultCulture);

    /// <summary>
    /// What the anonymous host-resolution endpoint returns, so the sign-in page can show the
    /// right name and logo before anybody types anything.
    ///
    /// Branding and status only. Nothing here says whether any particular account exists.
    /// </summary>
    public static TenantResolutionResponse ToResolutionResponse(
        Tenant? tenant, BusinessUnit businessUnit, bool isPlatformHost) =>
        tenant is null
            ? new TenantResolutionResponse(
                Resolved: isPlatformHost,
                TenantId: null,
                TenantCode: null,
                TenantName: null,
                Subdomain: null,
                Status: null,
                IsOperable: isPlatformHost,
                IsPlatformHost: isPlatformHost,
                LogoUrl: businessUnit.LogoUrl,
                BusinessUnitId: businessUnit.Id,
                BusinessUnitName: businessUnit.Name,
                Message: isPlatformHost
                    ? "Platform sign-in."
                    : "This address is not linked to an organisation.")
            : new TenantResolutionResponse(
                Resolved: true,
                tenant.Id,
                tenant.Code,
                tenant.Name,
                tenant.Subdomain,
                tenant.Status,
                tenant.IsOperable,
                IsPlatformHost: false,
                tenant.LogoUrl ?? businessUnit.LogoUrl,
                businessUnit.Id,
                businessUnit.Name,
                Message: tenant.IsOperable ? null : DescribeUnavailability(tenant.Status));

    /// <summary>
    /// What the activation screen shows before a password is typed. Deliberately thin: an
    /// unauthenticated caller holding a token should learn only enough to decide whether to
    /// go on.
    /// </summary>
    /// <summary>
    /// The reply for a link that is no good: unknown, spent or expired.
    ///
    /// It carries the platform branding and a sentence, and NOTHING ELSE. Somebody holding a bad
    /// token has proved nothing, so there is no name, no address and no Organisation to give
    /// them - and a reply that differed by reason would let the endpoint be walked to find out
    /// which tokens exist.
    ///
    /// It is a factory rather than three literals because the DTO has grown twice already, and
    /// each time the copies drifted before they were noticed.
    /// </summary>
    public static InvitationPreviewResponse InvalidInvitation(
        BusinessUnit businessUnit,
        SecuritySettings security,
        InvitationType invitationType = InvitationType.TenantUser,
        DateTimeOffset? expiresAtUtc = null,
        string message = "That invitation link is not valid.")
    {
        ArgumentNullException.ThrowIfNull(businessUnit);
        ArgumentNullException.ThrowIfNull(security);

        return new InvitationPreviewResponse(
            IsValid: false,
            Email: null, DisplayName: null, TenantName: null, TenantCode: null,
            BusinessUnitName: businessUnit.Name, LogoUrl: businessUnit.LogoUrl,
            invitationType, expiresAtUtc,
            RequiresOrganisationProfile: false,
            Message: message,
            Username: null, AccountCategory: null, Department: null, OrganisationUnit: null,
            Designation: null, InvitedRoleSummary: null,
            AccessStartsAtUtc: null, AccessEndsAtUtc: null,
            security.PasswordMinimumLength, security.PasswordMaximumLength,
            security.PasswordRequireUppercase, security.PasswordRequireLowercase,
            security.PasswordRequireDigit, security.PasswordRequireNonAlphanumeric,
            MfaMandatory: false,
            AllowedMfaMethods: [],

            // A dead link never reaches the enrolment step, so it needs no dialling prefixes.
            DialingCodes: []);
    }

    public static InvitationPreviewResponse ToPreviewResponse(
        UserInvitation invitation,
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        DateTimeOffset asOf,
        SecuritySettings security,
        string? roleSummary = null,
        string? departmentName = null,
        string? organisationUnitName = null,
        IReadOnlyList<string>? dialingCodes = null)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);
        ArgumentNullException.ThrowIfNull(security);

        // A dead link says so and says nothing else. Somebody holding an expired or already-used
        // token has not proved anything, so the reply carries no name, no address and no
        // Organisation - only enough to explain what to do next.
        if (!invitation.IsRedeemable(asOf))
        {
            return InvalidInvitation(
                businessUnit, security, invitation.InvitationType, invitation.ExpiresAtUtc,
                invitation.Status == InvitationStatus.Accepted
                    ? "That invitation has already been used. Sign in instead."
                    : "That invitation has expired. Ask your administrator to send a new one.");
        }

        var mfaRequirement = tenant?.DefaultMfaRequirement ?? MfaRequirement.Optional;

        return new InvitationPreviewResponse(
            IsValid: true,
            invitation.Email,
            user.DisplayName,
            tenant?.Name,
            tenant?.Code,
            businessUnit.Name,
            tenant?.LogoUrl ?? businessUnit.LogoUrl,
            invitation.InvitationType,
            invitation.ExpiresAtUtc,
            RequiresOrganisationProfile:
                invitation.InvitationType == InvitationType.TenantAdmin
                && tenant is not null && tenant.IsOnboarding,
            Message: null,
            user.UserName,
            user.AccountCategory.ToString(),
            departmentName,
            organisationUnitName,
            user.Designation,
            roleSummary,
            user.AccessStartsAtUtc,
            user.AccessEndsAtUtc,
            security.PasswordMinimumLength,
            security.PasswordMaximumLength,
            security.PasswordRequireUppercase,
            security.PasswordRequireLowercase,
            security.PasswordRequireDigit,
            security.PasswordRequireNonAlphanumeric,
            MfaMandatory: user.IsMfaRequired(mfaRequirement),

            // Security keys are not implemented yet, so offering one would be a dead end on the
            // screen. The list is what a person can actually finish enrolling today.
            AllowedMfaMethods: [MfaMethodType.AuthenticatorApp, MfaMethodType.Email, MfaMethodType.Sms],

            DialingCodes: dialingCodes ?? []);
    }

    /// <summary>The password rules, so the client strength meter matches what the server accepts.</summary>
    public static PasswordPolicyResponse ToPolicyResponse(this SecuritySettings settings, int? tenantMinimum = null) =>
        new(
            Math.Max(settings.PasswordMinimumLength, tenantMinimum ?? 0),
            settings.PasswordMaximumLength,
            settings.PasswordRequireUppercase,
            settings.PasswordRequireLowercase,
            settings.PasswordRequireDigit,
            settings.PasswordRequireNonAlphanumeric,
            settings.PasswordHistoryCount);

    private static string DescribeUnavailability(TenantStatus status) => status switch
    {
        TenantStatus.Suspended => "This organisation has been suspended. Contact support.",
        TenantStatus.Archived => "This organisation is no longer active.",
        TenantStatus.Rejected => "This organisation has not been approved yet.",
        _ => "This organisation is not active yet."
    };
}
