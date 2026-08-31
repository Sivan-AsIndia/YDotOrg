using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An Organisation. Called "Tenant" everywhere in the schema and the domain, and
/// "Organisation" everywhere a person can see it — that split is deliberate and comes
/// straight from the brief.
///
/// THE TENANT IS THE ISOLATION BOUNDARY. Every row that implements
/// <see cref="ITenantOwned"/> names one of these, and a query filter keyed on the current
/// request context means a read simply cannot cross from one to another. A Tenant itself
/// is NOT tenant-owned — it names its BusinessUnit instead, because it is the thing being
/// isolated rather than something inside the isolation.
///
/// LIFECYCLE. <see cref="Status"/> is a real state machine (see <see cref="TenantStatus"/>)
/// and <see cref="AllowedTransitionsFrom"/> is the only description of how it moves. The
/// brief is explicit that this must not collapse into an IsApproved boolean.
/// </summary>
public class Tenant : AuditEntity, IBusinessUnitOwned, ICodedEntity
{
    public Guid BusinessUnitId { get; set; }

    public BusinessUnit? BusinessUnit { get; set; }

    /// <summary>Unique inside the BusinessUnit, for example TEN001.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The name shown to people: "Hope Foundation".</summary>
    public string Name { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    /// <summary>
    /// The subdomain label only — "ten1", never "ten1.ngoplanet.com". The full host lives
    /// in <see cref="TenantDomain"/>, which is what allows a custom domain to be added
    /// later without touching this entity.
    ///
    /// Unique across the whole BusinessUnit, because two Organisations cannot share a host.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    public TenantStatus Status { get; set; } = TenantStatus.Invited;

    // ---- Organisation profile, completed by the TenantAdmin after activation -------------

    public string? RegistrationNumber { get; set; }

    public string? TaxIdentificationNumber { get; set; }

    /// <summary>India-specific but optional; left null elsewhere.</summary>
    public string? PanNumber { get; set; }

    public string? GstNumber { get; set; }

    public string? OrganisationType { get; set; }

    public DateTimeOffset? EstablishedOn { get; set; }

    public string? Description { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? LogoUrl { get; set; }

    // ---- Primary contact ------------------------------------------------------------------

    public string? ContactPersonName { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhoneCountryCode { get; set; }

    public string? ContactPhone { get; set; }

    // ---- Registered address ---------------------------------------------------------------

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Country { get; set; }

    public string? PostalCode { get; set; }

    // ---- Localisation, inherited from the BusinessUnit at creation --------------------------

    public string TimeZone { get; set; } = "Asia/Kolkata";

    public string DefaultCurrency { get; set; } = "INR";

    public string DefaultCulture { get; set; } = "en-IN";

    // ---- Tenant-level security policy -------------------------------------------------------

    /// <summary>What <see cref="MfaRequirement.Inherited"/> on a user resolves to.</summary>
    public MfaRequirement DefaultMfaRequirement { get; set; } = MfaRequirement.Optional;

    /// <summary>Failed sign-ins before the account locks. The brief asks for 5.</summary>
    public int MaximumFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts. The brief asks for 15 minutes.</summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    public int PasswordMinimumLength { get; set; } = 10;

    /// <summary>Zero disables expiry, which is the current guidance for most organisations.</summary>
    public int PasswordExpiryDays { get; set; }

    public int SessionIdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Ceiling on Tenant users. Null means no ceiling.</summary>
    public int? MaximumUsers { get; set; }

    // ---- Lifecycle bookkeeping ----------------------------------------------------------------

    public DateTimeOffset? InvitedAtUtc { get; set; }

    public DateTimeOffset? InvitationAcceptedAtUtc { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public Guid? SubmittedByUserId { get; set; }

    public DateTimeOffset? ReviewStartedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? RejectedAtUtc { get; set; }

    public Guid? RejectedByUserId { get; set; }

    /// <summary>Required whenever the Organisation is rejected, so the TenantAdmin can fix it.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset? ActivatedAtUtc { get; set; }

    public DateTimeOffset? SuspendedAtUtc { get; set; }

    public string? SuspensionReason { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    /// <summary>How many times this Organisation has been sent back and resubmitted.</summary>
    public int ResubmissionCount { get; set; }

    // ---- Navigations ---------------------------------------------------------------------------

    public ICollection<TenantDomain> Domains { get; set; } = [];

    public ICollection<TenantDocument> Documents { get; set; } = [];

    /// <summary>Grouped document submissions — the unit a reviewer decides on.</summary>
    public ICollection<TenantDocumentSubmission> DocumentSubmissions { get; set; } = [];

    public ICollection<TenantStatusHistory> StatusHistory { get; set; } = [];

    // ---- Derived state --------------------------------------------------------------------------

    /// <summary>
    /// The only status in which a Tenant user may sign in. Every other status — including
    /// Approved, which means "accepted but not switched on yet" — refuses authentication.
    /// </summary>
    public bool IsOperable => Status == TenantStatus.Active;

    /// <summary>True once SuperAdmin has accepted the Organisation, whether or not it is live.</summary>
    public bool IsApproved => IsApprovedStatus(Status);

    /// <summary>True while the Organisation is still working through onboarding.</summary>
    public bool IsOnboarding => IsOnboardingStatus(Status);

    /// <summary>
    /// The same two questions, answered from a bare status.
    ///
    /// The navigation builder and the request gate both need "is this Organisation approved
    /// yet?" and neither of them has a <see cref="Tenant"/> to hand - they hold the status
    /// from the request context. Keeping the predicates here rather than restating the status
    /// lists at each call site is what stops a new status being added to one copy and not the
    /// other, which would leave a half-onboarded Organisation with a full sidebar.
    /// </summary>
    public static bool IsApprovedStatus(TenantStatus status) =>
        status is TenantStatus.Approved or TenantStatus.Active;

    /// <inheritdoc cref="IsApprovedStatus"/>
    public static bool IsOnboardingStatus(TenantStatus status) =>
        status is TenantStatus.Invited or TenantStatus.InvitationAccepted
            or TenantStatus.ProfileIncomplete or TenantStatus.Submitted or TenantStatus.UnderReview
            or TenantStatus.Rejected or TenantStatus.Resubmitted;

    /// <summary>
    /// May somebody holding a session in this Organisation keep using it?
    /// </summary>
    /// <remarks>
    /// IF YOU ARE NEW TO THIS CODEBASE, START HERE - this one method is the whole "who may be
    /// signed in" rule, and it is asked in two different places that used to disagree.
    ///
    /// There are two moments where the question comes up:
    ///
    /// <list type="number">
    /// <item>SIGNING IN. You type a password and we decide whether to issue you a token.</item>
    /// <item>REFRESHING. Your access token is short-lived - minutes, not days - so the browser
    /// quietly trades a refresh token for a new one in the background. That is a SECOND chance
    /// to say no, and it is the reason a suspension takes effect within minutes instead of
    /// whenever you next happen to sign in.</item>
    /// </list>
    ///
    /// THOSE TWO CHECKS MUST AGREE, and for a while they did not. Sign-in let the TenantAdmin
    /// of a half-finished Organisation in - they have to be let in, because completing the
    /// profile is a job only they can do - but the refresh path asked a stricter question and
    /// threw the same person out a few minutes later. The visible bug was an administrator
    /// being signed out, seemingly at random, in the middle of filling in a form. Both callers
    /// now ask THIS method, so the two answers cannot drift apart again.
    ///
    /// The rule itself, in plain terms:
    ///
    /// <list type="bullet">
    /// <item>APPROVED or ACTIVE - anybody may be here. The Organisation has been accepted.</item>
    /// <item>Still onboarding - only the administrator, because only they can finish it. Their
    /// staff have nothing to do in an Organisation that is not live yet.</item>
    /// <item>SUSPENDED or ARCHIVED - nobody, administrator included. These are decisions ABOUT
    /// the Organisation rather than steps on the way in, so the administrator is not special.</item>
    /// </list>
    ///
    /// SuperAdmin is not mentioned because they are not governed by this at all: they are
    /// platform staff visiting an Organisation, and each caller exempts them separately.
    /// </remarks>
    /// <param name="isTenantAdmin">
    /// Whether the person is this Organisation's own administrator. Passed in rather than read
    /// from a User, because <see cref="Tenant"/> deliberately knows nothing about users.
    /// </param>
    public bool PermitsSession(bool isTenantAdmin) =>
        IsApproved || (isTenantAdmin && IsOnboarding);

    /// <summary>True when the TenantAdmin may still edit the profile.</summary>
    public bool IsProfileEditable => Status is TenantStatus.InvitationAccepted
        or TenantStatus.ProfileIncomplete or TenantStatus.Rejected;

    /// <summary>True when SuperAdmin has something waiting on their desk.</summary>
    public bool IsAwaitingReview => Status is TenantStatus.Submitted or TenantStatus.Resubmitted
        or TenantStatus.UnderReview;

    /// <summary>
    /// The legal moves out of each status, in one place. Keeping the table here rather than
    /// scattering it across handlers is what makes "Invited straight to Active" impossible
    /// to write by accident.
    /// </summary>
    public static IReadOnlyList<TenantStatus> AllowedTransitionsFrom(TenantStatus status) => status switch
    {
        TenantStatus.Invited => [TenantStatus.InvitationAccepted, TenantStatus.Archived],
        TenantStatus.InvitationAccepted => [TenantStatus.ProfileIncomplete, TenantStatus.Submitted, TenantStatus.Archived],
        TenantStatus.ProfileIncomplete => [TenantStatus.Submitted, TenantStatus.Archived],
        TenantStatus.Submitted => [TenantStatus.UnderReview, TenantStatus.Approved, TenantStatus.Rejected],
        TenantStatus.UnderReview => [TenantStatus.Approved, TenantStatus.Rejected],
        TenantStatus.Rejected => [TenantStatus.Resubmitted, TenantStatus.Archived],
        TenantStatus.Resubmitted => [TenantStatus.UnderReview, TenantStatus.Approved, TenantStatus.Rejected],
        TenantStatus.Approved => [TenantStatus.Active, TenantStatus.Suspended, TenantStatus.Archived],
        TenantStatus.Active => [TenantStatus.Suspended, TenantStatus.Archived],
        TenantStatus.Suspended => [TenantStatus.Active, TenantStatus.Archived],
        TenantStatus.Archived => [],
        _ => []
    };

    public bool CanTransitionTo(TenantStatus target) => AllowedTransitionsFrom(Status).Contains(target);
}
