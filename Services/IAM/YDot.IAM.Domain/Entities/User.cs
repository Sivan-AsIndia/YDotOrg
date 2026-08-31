using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A person who can sign in. The central aggregate of the IAM section.
///
/// THIS IS A CUSTOMISED IdentityCore TABLE. It derives from
/// <see cref="IdentityUser{TKey}"/> with a Guid key, so the framework supplies the
/// credential machinery and this class adds only what YDot genuinely needs on top. What
/// comes from the base, and therefore is deliberately NOT redeclared here:
///
/// <code>
/// Id                     UserName            NormalizedUserName
/// Email                  NormalizedEmail     EmailConfirmed
/// PasswordHash           SecurityStamp       ConcurrencyStamp
/// PhoneNumber            PhoneNumberConfirmed
/// TwoFactorEnabled       LockoutEnd          LockoutEnabled
/// AccessFailedCount
/// </code>
///
/// Using the real base class rather than hand-rolled equivalents means UserManager,
/// SignInManager, PasswordHasher and the token providers all work against this entity
/// unchanged, and the hashing algorithm improves with the framework instead of rotting.
///
/// THE ONE RULE THAT MATTERS. A normal user belongs to exactly one Organisation. The same
/// e-mail may exist in several Organisations as several genuinely separate people:
///
/// <code>
/// TEN001   john@gmail.com -> U101
/// TEN002   john@gmail.com -> U201
/// </code>
///
/// Two rows, two passwords, two sets of roles, and accepting an invitation to one must never
/// touch the other. IdentityCore normally puts a UNIQUE index on NormalizedEmail and
/// NormalizedUserName, which would make that impossible — so the EF configuration REPLACES
/// those with composite indexes on (TenantId, NormalizedEmail) and
/// (TenantId, NormalizedUserName). That replacement is the single most important
/// customisation in this file.
///
/// SUPERADMIN IS THE EXCEPTION. SuperAdmin has <see cref="TenantId"/> = null forever. They
/// are not a member of any Organisation; they select one to operate in, and that selection
/// lives in the token as request context. Nothing here is written when a Tenant is selected.
/// </summary>
public class User : IdentityUser<Guid>, IAuditable, ITenantScoped
{
    public User()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    // ---- Audit, from IAuditable. Stamped by the DbContext. ----------------------------------

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public long Version { get; set; } = 1;

    // ---- Tenancy ------------------------------------------------------------------------------

    /// <summary>
    /// The owning Organisation, or null for the global SuperAdmin. Never changed by a Tenant
    /// selection.
    /// </summary>
    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    // ---- Section 3.1 property contract, minus what IdentityUser already gives us --------------

    /// <summary>Unique inside the Tenant. Doubles as the staff-facing display code, USR-00042.</summary>
    public string Code { get; set; } = string.Empty;

    public string? EmployeeNumber { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Set when <see cref="IdentityUser{TKey}.EmailConfirmed"/> flips true. The base carries
    /// only the boolean, and an auditor almost always wants the date as well.
    /// </summary>
    public DateTimeOffset? EmailConfirmedAtUtc { get; set; }

    /// <summary>
    /// E.164 country prefix, held apart from the subscriber digits. The base
    /// <c>PhoneNumber</c> is a single string; the section 3.1 contract splits it into
    /// <c>MobileCountryCode</c> and <c>MobileNumber</c>, and the split is what lets a screen
    /// show a country picker beside a national number.
    /// </summary>
    public string? MobileCountryCode { get; set; }

    /// <summary>
    /// Subscriber digits. Kept in step with the base <c>PhoneNumber</c>, which holds the
    /// joined E.164 form for the framework SMS token providers to use.
    /// </summary>
    public string? MobileNumber { get; set; }

    /// <summary>
    /// Whether the mobile number has been proved.
    ///
    /// A FACADE over the inherited <see cref="IdentityUser{TKey}.PhoneNumberConfirmed"/>, not
    /// a second column - for the same reason as <see cref="MfaEnabled"/>. Two flags for one
    /// fact is how a number ends up confirmed according to one code path and not the other,
    /// and the framework SMS providers only ever consult PhoneNumberConfirmed.
    ///
    /// The name matches the section 3.1 vocabulary, which says Mobile rather than Phone.
    /// Not mapped by EF - the column is phone_number_confirmed.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool MobileConfirmed
    {
        get => PhoneNumberConfirmed;
        set => PhoneNumberConfirmed = value;
    }

    public UserAccountCategory AccountCategory { get; set; } = UserAccountCategory.Employee;

    public Guid? DepartmentId { get; set; }

    public Department? Department { get; set; }

    public Guid? OrganisationUnitId { get; set; }

    public OrganisationUnit? OrganisationUnit { get; set; }

    public string? Designation { get; set; }

    /// <summary>Cannot reference this user. Must belong to the same Tenant.</summary>
    public Guid? ManagerUserId { get; set; }

    public User? Manager { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Draft;

    public DateTimeOffset AccessStartsAtUtc { get; set; }

    /// <summary>Must be later than the start. Null means open-ended.</summary>
    public DateTimeOffset? AccessEndsAtUtc { get; set; }

    /// <summary>
    /// Three-state, unlike the base <c>TwoFactorEnabled</c> boolean: Inherited defers to the
    /// Organisation policy, so an Organisation can turn MFA on for everybody who has not made
    /// an explicit choice. <c>TwoFactorEnabled</c> is kept in step with the resolved answer so
    /// the framework sign-in path still behaves.
    /// </summary>
    public MfaRequirement MfaRequirement { get; set; } = MfaRequirement.Inherited;

    public DateTimeOffset? LastSignedInAtUtc { get; set; }

    // ---- Engagement -----------------------------------------------------------------------------

    public EngagementType EngagementType { get; set; } = EngagementType.FullTime;

    public DateTimeOffset? JoinedOn { get; set; }

    public DateTimeOffset? ExitedOn { get; set; }

    // ---- Credentials, on top of the base PasswordHash and SecurityStamp -------------------------

    public DateTimeOffset? PasswordChangedAtUtc { get; set; }

    /// <summary>Set when an administrator issues a temporary password. Blocks everything
    /// except the change-password endpoint until it is cleared.</summary>
    public bool MustChangePassword { get; set; }

    public CredentialSetupMethod CredentialSetupMethod { get; set; } = CredentialSetupMethod.InvitationLink;

    // ---- Lockout. The base gives AccessFailedCount, LockoutEnd and LockoutEnabled. ---------------

    /// <summary>
    /// Set when an administrator locks the account by hand, as opposed to the failure counter
    /// tripping. The base has no way to express the difference, and the two need different
    /// messages and different remedies.
    /// </summary>
    public bool IsLockedOutByAdministrator { get; set; }

    public string? LockoutReason { get; set; }

    // ---- Last-session capture, required by the brief ------------------------------------------------

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public string? LastLoginIpAddress { get; set; }

    public string? LastLoginUserAgent { get; set; }

    public ClientType LastLoginClientType { get; set; } = ClientType.Unknown;

    /// <summary>Parsed from the user agent for display: "Chrome", "Firefox".</summary>
    public string? LastLoginBrowser { get; set; }

    public string? LastLoginOperatingSystem { get; set; }

    /// <summary>
    /// Hardware identifier reported by a mobile client. Named IMEI in the brief; in practice a
    /// modern device sends a vendor install identifier, because the real IMEI is no longer
    /// readable by an ordinary app on either platform. Stored as given, never used as an
    /// authentication factor on its own.
    /// </summary>
    public string? LastLoginDeviceIdentifier { get; set; }

    public DateTimeOffset? LastFailedLoginAtUtc { get; set; }

    public string? LastFailedLoginIpAddress { get; set; }

    // ---- MFA, beyond the base TwoFactorEnabled ---------------------------------------------------------

    /// <summary>
    /// Whether a second factor is actually enrolled and in force.
    ///
    /// This is a FACADE over the inherited <see cref="IdentityUser{TKey}.TwoFactorEnabled"/>,
    /// not a second column - reading and writing it reads and writes that one. Two separate
    /// flags for the same fact is how a user ends up enrolled according to one code path and
    /// not the other, and the framework sign-in path only ever consults TwoFactorEnabled.
    ///
    /// The name is kept because "MFA" is the vocabulary the brief, the screens and the rest of
    /// this codebase use; TwoFactorEnabled is simply what the framework happens to call it.
    /// Not mapped by EF - the column is two_factor_enabled.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool MfaEnabled
    {
        get => TwoFactorEnabled;
        set => TwoFactorEnabled = value;
    }

    public DateTimeOffset? MfaEnrolledAtUtc { get; set; }

    /// <summary>
    /// Base32 secret for the authenticator app, encrypted at rest by the DbContext value
    /// converter. IdentityCore would normally keep this in <c>AspNetUserTokens</c>; it is a
    /// column here so enrolment state can be read without a second query on the sign-in path.
    /// </summary>
    public string? AuthenticatorSecret { get; set; }

    /// <summary>How many single-use recovery codes are still unspent.</summary>
    public int RecoveryCodesRemaining { get; set; }

    // ---- Privilege ---------------------------------------------------------------------------------------

    /// <summary>
    /// The coarse tier this user sits in. <see cref="PrivilegeLevel.SuperAdmin"/> is the only
    /// value that may cross a Tenant boundary, and the only one permitted alongside a null
    /// <see cref="TenantId"/>.
    /// </summary>
    public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.Standard;

    /// <summary>
    /// True for the root user. Its own column rather than derived from
    /// <see cref="PrivilegeLevel"/> so the check constraint tying it to a null TenantId can be
    /// expressed in the database, where it cannot be forgotten.
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>
    /// True for the first administrator of an Organisation. Several users may hold the
    /// TenantAdmin role over time; this marks the one the Organisation was created with, who
    /// the onboarding e-mails and the approval result are addressed to.
    /// </summary>
    public bool IsTenantAdmin { get; set; }

    /// <summary>System accounts cannot be deleted or have their privilege reduced through the UI.</summary>
    public bool IsSystemAccount { get; set; }

    // ---- Preferences ---------------------------------------------------------------------------------------

    public string? PreferredCulture { get; set; }

    public string? TimeZone { get; set; }

    public string? AvatarUrl { get; set; }

    // ---- Navigations ------------------------------------------------------------------------------------------

    public ICollection<UserRole> UserRoles { get; set; } = [];

    public ICollection<UserSession> Sessions { get; set; } = [];

    public ICollection<MfaMethod> MfaMethods { get; set; } = [];

    public ICollection<UserDataScope> DataScopes { get; set; } = [];

    public ICollection<UserClaimEntry> Claims { get; set; } = [];

    public ICollection<UserLogin> Logins { get; set; } = [];

    // ---- Derived state -----------------------------------------------------------------------------------------

    /// <summary>True while a lockout is in force at the given moment.</summary>
    public bool IsLockedOut(DateTimeOffset asOf) =>
        IsLockedOutByAdministrator || (LockoutEnabled && LockoutEnd.HasValue && LockoutEnd.Value > asOf);

    /// <summary>Whole minutes left on the lockout, rounded up, for the message on the screen.</summary>
    public int LockoutMinutesRemaining(DateTimeOffset asOf) =>
        LockoutEnd.HasValue && LockoutEnd.Value > asOf
            ? (int)Math.Ceiling((LockoutEnd.Value - asOf).TotalMinutes)
            : 0;

    /// <summary>True when the access window has not opened yet or has already closed.</summary>
    public bool IsOutsideAccessWindow(DateTimeOffset asOf) =>
        AccessStartsAtUtc > asOf || (AccessEndsAtUtc.HasValue && AccessEndsAtUtc.Value < asOf);

    /// <summary>
    /// Everything that has to be true before a password is even worth checking. The Tenant
    /// status is deliberately NOT part of this: it is a separate check with its own message,
    /// because "your Organisation is suspended" and "your account is suspended" send the
    /// person to two different people for help.
    /// </summary>
    public bool CanAttemptSignIn(DateTimeOffset asOf) =>
        Status == UserStatus.Active
        && PasswordHash is not null
        && !IsLockedOut(asOf)
        && !IsOutsideAccessWindow(asOf);

    /// <summary>Resolves an Inherited requirement against the Organisation policy.</summary>
    public bool IsMfaRequired(MfaRequirement tenantDefault) =>
        MfaRequirement switch
        {
            Enums.MfaRequirement.Required => true,
            Enums.MfaRequirement.Optional => false,
            _ => tenantDefault == Enums.MfaRequirement.Required
        };

    public string FullName =>
        string.Join(' ', new[] { FirstName, MiddleName, LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>The joined E.164 number the framework SMS providers expect.</summary>
    public string? ToE164() =>
        string.IsNullOrWhiteSpace(MobileCountryCode) || string.IsNullOrWhiteSpace(MobileNumber)
            ? null
            : MobileCountryCode + MobileNumber;
}
