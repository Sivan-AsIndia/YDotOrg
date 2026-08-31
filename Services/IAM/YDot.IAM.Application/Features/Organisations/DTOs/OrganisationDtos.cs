using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.DTOs;

// =====================================================================================
// Creation — SuperAdmin creates the Organisation and invites its first administrator
// =====================================================================================

/// <summary>
/// SuperAdmin creating an Organisation.
///
/// ONE CALL DOES THREE THINGS, and they belong together: the Organisation row, its primary
/// host mapping, and the TenantAdmin user plus their invitation. Splitting them across three
/// endpoints would let an Organisation exist with no administrator and no way to reach it,
/// which is a state nothing in the product knows what to do with.
///
/// <paramref name="Subdomain"/> is the label only — "ten1", not "ten1.ngoplanet.com". The
/// full host is composed from the BusinessUnit root domain.
/// </summary>
public sealed record CreateOrganisationRequest(
    string Name,
    string Subdomain,
    string AdminEmail,
    string AdminFirstName,
    string AdminLastName,
    string? Code = null,
    string? LegalName = null,
    string? OrganisationType = null,
    string? ContactPhoneCountryCode = null,
    string? ContactPhone = null,
    string? AdminUsername = null,
    string? TimeZone = null,
    string? DefaultCurrency = null,
    string? DefaultCulture = null,
    int? MaximumUsers = null,
    MfaRequirement DefaultMfaRequirement = MfaRequirement.Optional,
    string? InvitationMessage = null,

    /// <summary>
    /// Skips the invitation e-mail and leaves the Organisation Invited. For bulk imports
    /// where the invitations go out later on a schedule.
    /// </summary>
    bool SendInvitation = true);

/// <summary>The Organisation as created, plus what happened to the invitation.</summary>
public sealed record CreateOrganisationResponse(
    Guid TenantId,
    string Code,
    string Name,
    string Subdomain,
    string HostName,
    TenantStatus Status,
    Guid AdminUserId,
    string AdminEmail,
    bool InvitationSent,
    DateTimeOffset? InvitationExpiresAtUtc,

    /// <summary>
    /// Present only when the mail relay is switched off, so a developer can still walk the
    /// flow. Never populated when e-mail is enabled — a live activation link in an API
    /// response is a link in a log file.
    /// </summary>
    string? ActivationUrl,

    long Version);

// =====================================================================================
// Profile — the TenantAdmin fills this in after activating
// =====================================================================================

/// <summary>
/// The Organisation profile.
///
/// Everything is optional at the field level and enforced at SUBMISSION instead, so the
/// TenantAdmin can save a half-finished profile and come back to it. Requiring every field on
/// every save would mean losing work whenever somebody has to go and find a certificate.
/// </summary>
public sealed record UpdateOrganisationProfileRequest(
    long ExpectedVersion,
    string? Name = null,
    string? LegalName = null,
    string? RegistrationNumber = null,
    string? TaxIdentificationNumber = null,
    string? PanNumber = null,
    string? GstNumber = null,
    string? OrganisationType = null,
    DateTimeOffset? EstablishedOn = null,
    string? Description = null,
    string? WebsiteUrl = null,
    string? LogoUrl = null,
    string? ContactPersonName = null,
    string? ContactEmail = null,
    string? ContactPhoneCountryCode = null,
    string? ContactPhone = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? PostalCode = null,
    string? TimeZone = null,
    string? DefaultCurrency = null,
    string? DefaultCulture = null);

/// <summary>Submits the profile for SuperAdmin approval.</summary>
public sealed record SubmitOrganisationRequest(long ExpectedVersion, string? Notes = null);

/// <summary>SuperAdmin picking a submission up, so the queue shows it is being looked at.</summary>
public sealed record StartOrganisationReviewRequest(long ExpectedVersion);

/// <summary>
/// SuperAdmin approving or rejecting. <c>Reason</c> is mandatory on a rejection: a refusal
/// the TenantAdmin cannot act on is a dead end rather than a decision.
/// </summary>
public sealed record ReviewOrganisationRequest(
    bool Approved,
    long ExpectedVersion,
    string? Reason = null,
    string? Notes = null,

    /// <summary>Activates immediately on approval, rather than leaving it for a second step.</summary>
    bool ActivateImmediately = true);

/// <summary>Suspending a live Organisation. Sign-in stops; data is retained.</summary>
public sealed record SuspendOrganisationRequest(string Reason, long ExpectedVersion);

/// <summary>Lifting a suspension.</summary>
public sealed record ReactivateOrganisationRequest(long ExpectedVersion, string? Notes = null);

/// <summary>Retiring an Organisation. Terminal.</summary>
public sealed record ArchiveOrganisationRequest(string Reason, long ExpectedVersion);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the Organisation directory. Intentionally compact.</summary>
public sealed record OrganisationListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string Subdomain,
    string HostName,
    TenantStatus Status,
    string StatusDisplay,
    string? LogoUrl,
    string? Country,
    int UserCount,
    string? AdminEmail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    bool IsAwaitingReview,
    long Version);

/// <summary>
/// The full Organisation record.
///
/// <paramref name="PermittedActions"/> is what the record STATE allows, which is not the same
/// as what the CALLER may do — permission is checked separately on each endpoint. The client
/// uses it to decide which buttons to draw, so a TenantAdmin does not see an Approve button
/// that would answer 403.
/// </summary>
public sealed record OrganisationDetailResponse(
    Guid Id,
    Guid BusinessUnitId,
    string BusinessUnitName,
    string Code,
    string Name,
    string? LegalName,
    string Subdomain,
    string HostName,
    TenantStatus Status,
    string StatusDisplay,

    string? RegistrationNumber,
    string? TaxIdentificationNumber,
    string? PanNumber,
    string? GstNumber,
    string? OrganisationType,
    DateTimeOffset? EstablishedOn,
    string? Description,
    string? WebsiteUrl,
    string? LogoUrl,

    string? ContactPersonName,
    string? ContactEmail,
    string? ContactPhoneCountryCode,
    string? ContactPhone,

    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? Country,
    string? PostalCode,

    string TimeZone,
    string DefaultCurrency,
    string DefaultCulture,

    MfaRequirement DefaultMfaRequirement,
    int MaximumFailedAccessAttempts,
    int LockoutDurationMinutes,
    int PasswordMinimumLength,
    int PasswordExpiryDays,
    int SessionIdleTimeoutMinutes,
    int? MaximumUsers,
    int UserCount,

    DateTimeOffset? InvitedAtUtc,
    DateTimeOffset? InvitationAcceptedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    string? RejectionReason,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? SuspendedAtUtc,
    string? SuspensionReason,
    int ResubmissionCount,

    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,

    IReadOnlyList<OrganisationDomainResponse> Domains,
    IReadOnlyList<OrganisationDocumentResponse> Documents,
    IReadOnlyList<OrganisationTimelineResponse> Timeline,
    OrganisationAdminResponse? PrimaryAdmin,

    /// <summary>What the current status allows. Permission is checked separately.</summary>
    IReadOnlyList<string> PermittedActions,

    /// <summary>Fields still needed before the profile can be submitted.</summary>
    IReadOnlyList<string> OutstandingProfileFields,

    bool IsProfileComplete);

/// <summary>A host name that reaches this Organisation.</summary>
public sealed record OrganisationDomainResponse(
    Guid Id,
    string HostName,
    TenantDomainType DomainType,
    bool IsPrimary,
    bool IsVerified,
    bool IsActive,
    DateTimeOffset? VerifiedAtUtc,
    string? VerificationToken);

/// <summary>One uploaded document and its review state.</summary>
public sealed record OrganisationDocumentResponse(
    Guid Id,
    TenantDocumentType DocumentType,
    string DocumentTypeDisplay,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    TenantDocumentStatus Status,
    string? ReferenceNumber,
    DateTimeOffset? IssuedOn,
    DateTimeOffset? ExpiresOn,
    bool IsExpired,
    DateTimeOffset UploadedAtUtc,
    string? UploadedByName,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewedByName,
    string? ReviewNotes,

    /// <summary>
    /// The grouped submission this file belongs to, when it has one.
    ///
    /// NULL MEANS LEGACY. Files uploaded before grouped submissions existed have no parent, and
    /// they are still real evidence somebody attached - so the review screen lists them under
    /// their own heading rather than dropping them. It is also what stops them appearing twice:
    /// anything WITH a submission is drawn by the submissions component instead.
    /// </summary>
    Guid? SubmissionId);

/// <summary>One rung of the lifecycle ladder, for the timeline on the detail screen.</summary>
public sealed record OrganisationTimelineResponse(
    Guid Id,
    TenantStatus? FromStatus,
    TenantStatus ToStatus,
    string ToStatusDisplay,
    DateTimeOffset OccurredAtUtc,
    string? ActorDisplayName,
    string? Reason,
    string? Notes);

/// <summary>The Organisation first administrator, for the directory and detail screens.</summary>
public sealed record OrganisationAdminResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    UserStatus Status,
    bool HasActivated,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? InvitationExpiresAtUtc,
    bool InvitationPending);

/// <summary>Counts by status, for the SuperAdmin dashboard tiles.</summary>
public sealed record OrganisationStatisticsResponse(
    int Total,
    int Active,
    int AwaitingReview,
    int Onboarding,
    int Suspended,
    int Archived,
    int Rejected,
    IReadOnlyDictionary<string, int> ByStatus);

// =====================================================================================
// Domains and documents
// =====================================================================================

/// <summary>Adds a host name. A custom domain arrives unverified with a token to publish.</summary>
public sealed record AddOrganisationDomainRequest(
    string HostName,
    TenantDomainType DomainType = TenantDomainType.CustomDomain,
    bool IsPrimary = false);

/// <summary>Confirms that the DNS record proving ownership has appeared.</summary>
public sealed record VerifyOrganisationDomainRequest(Guid DomainId);


/// <summary>SuperAdmin accepting or rejecting one document during review.</summary>
public sealed record ReviewOrganisationDocumentRequest(
    Guid DocumentId,
    bool Accepted,
    string? Notes = null);

/// <summary>
/// Checks whether a subdomain is free, before the create form is submitted.
///
/// Anonymous-safe by design: it answers only "available or not" and never lists what is
/// taken, so it cannot be walked to enumerate the platform customers.
/// </summary>
public sealed record CheckSubdomainRequest(string Subdomain);

/// <summary>The availability answer, with a suggestion when the wanted one is gone.</summary>
public sealed record CheckSubdomainResponse(
    string Subdomain,
    bool IsAvailable,
    bool IsReserved,
    bool IsValidFormat,
    string? HostName,
    string? Message,
    IReadOnlyList<string> Suggestions);

// =====================================================================================
// BusinessUnit — the root
// =====================================================================================

/// <summary>The BusinessUnit as the platform settings screen shows it.</summary>
public sealed record BusinessUnitResponse(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    string RootDomain,
    BusinessUnitStatus Status,
    string? ContactEmail,
    string? ContactPhone,
    string? SupportEmail,
    string? LogoUrl,
    string TimeZone,
    string DefaultCurrency,
    string DefaultCulture,
    int? MaximumTenants,
    int TenantCount,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>Editing the BusinessUnit.</summary>
public sealed record UpdateBusinessUnitRequest(
    long ExpectedVersion,
    string? Name = null,
    string? LegalName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? SupportEmail = null,
    string? LogoUrl = null,
    string? TimeZone = null,
    string? DefaultCurrency = null,
    string? DefaultCulture = null,
    int? MaximumTenants = null,
    string? Description = null);

/// <summary>
/// The Organisation security policy, edited from inside the Organisation.
///
/// An Organisation may TIGHTEN these but not loosen them below the platform floor, which the
/// handler enforces. Otherwise "manage your own settings" would become a way to disable
/// lockout entirely.
/// </summary>
public sealed record UpdateOrganisationSettingsRequest(
    long ExpectedVersion,
    MfaRequirement? DefaultMfaRequirement = null,
    int? MaximumFailedAccessAttempts = null,
    int? LockoutDurationMinutes = null,
    int? PasswordMinimumLength = null,
    int? PasswordExpiryDays = null,
    int? SessionIdleTimeoutMinutes = null);
