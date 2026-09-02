using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.Mappings;

/// <summary>
/// Manual mapping for the Organisation slice. Plain extension methods rather than a mapping
/// library: the rules are visible, debuggable, and nothing is discovered at run time.
/// </summary>
public static class OrganisationMappingConfig
{
    /// <summary>
    /// Request to new aggregate. The caller never sets identity, status, audit columns or the
    /// lifecycle timestamps — those belong to the platform, and accepting them from a body
    /// would let somebody create an Organisation that claims to be already approved.
    /// </summary>
    public static Tenant ToEntity(
        this CreateOrganisationRequest request,
        BusinessUnit businessUnit,
        string code,
        string subdomain,
        DateTimeOffset now) =>
        new()
        {
            BusinessUnitId = businessUnit.Id,
            Code = code,
            Name = request.Name.Trim(),
            LegalName = request.LegalName?.Trim(),
            Subdomain = subdomain,
            Status = TenantStatus.Invited,
            OrganisationType = request.OrganisationType?.Trim(),
            ContactPhoneCountryCode = request.ContactPhoneCountryCode?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            ContactEmail = request.AdminEmail.Trim().ToLowerInvariant(),

            // Localisation is inherited from the BusinessUnit unless the caller overrode it,
            // so a new Organisation is never left with a blank time zone.
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? businessUnit.TimeZone : request.TimeZone.Trim(),
            DefaultCurrency = string.IsNullOrWhiteSpace(request.DefaultCurrency)
                ? businessUnit.DefaultCurrency
                : request.DefaultCurrency.Trim().ToUpperInvariant(),
            DefaultCulture = string.IsNullOrWhiteSpace(request.DefaultCulture)
                ? businessUnit.DefaultCulture
                : request.DefaultCulture.Trim(),

            DefaultMfaRequirement = request.DefaultMfaRequirement,
            MaximumUsers = request.MaximumUsers,
            InvitedAtUtc = now
        };

    /// <summary>
    /// Applies a profile edit in place.
    ///
    /// Every field is null-guarded, so a partial update touches only what it names. A screen
    /// that posts three fields must not silently blank the other twenty.
    /// </summary>
    public static void ApplyProfile(this UpdateOrganisationProfileRequest request, Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tenant);

        tenant.Name = Coalesce(request.Name, tenant.Name)!;
        tenant.LegalName = Coalesce(request.LegalName, tenant.LegalName);
        tenant.RegistrationNumber = Coalesce(request.RegistrationNumber, tenant.RegistrationNumber);
        tenant.TaxIdentificationNumber = Coalesce(request.TaxIdentificationNumber, tenant.TaxIdentificationNumber);
        tenant.PanNumber = Coalesce(request.PanNumber, tenant.PanNumber)?.ToUpperInvariant();
        tenant.GstNumber = Coalesce(request.GstNumber, tenant.GstNumber)?.ToUpperInvariant();
        tenant.OrganisationType = Coalesce(request.OrganisationType, tenant.OrganisationType);
        tenant.EstablishedOn = request.EstablishedOn ?? tenant.EstablishedOn;
        tenant.Description = Coalesce(request.Description, tenant.Description);
        tenant.WebsiteUrl = Coalesce(request.WebsiteUrl, tenant.WebsiteUrl);
        tenant.LogoUrl = Coalesce(request.LogoUrl, tenant.LogoUrl);

        tenant.ContactPersonName = Coalesce(request.ContactPersonName, tenant.ContactPersonName);

        tenant.TimeZone = Coalesce(request.TimeZone, tenant.TimeZone)!;
        tenant.DefaultCurrency = Coalesce(request.DefaultCurrency, tenant.DefaultCurrency)!.ToUpperInvariant();
        tenant.DefaultCulture = Coalesce(request.DefaultCulture, tenant.DefaultCulture)!;

        request.ApplyContactAndAddress(tenant);
    }

    /// <summary>
    /// Applies ONLY the reachability fields — contact e-mail, telephone and postal address.
    ///
    /// THIS IS THE WHOLE EDIT ONCE THE ORGANISATION HAS BEEN APPROVED. Everything else on the
    /// profile — the name, the legal name, the registration number, PAN, GSTIN, the type — is
    /// what SuperAdmin checked the registration certificate against before approving. Leaving
    /// those writable afterwards means an Organisation can be approved as one legal entity and
    /// then quietly become another, with the accepted documents still attached and the timeline
    /// showing nothing but "profile saved".
    ///
    /// An address and a telephone number genuinely do change while an Organisation is running,
    /// and nothing downstream is verified against them, so they stay open. Correcting a name or
    /// a registration number is a re-verification, not an edit, and has to go back through
    /// review.
    /// </summary>
    public static void ApplyContactAndAddress(this UpdateOrganisationProfileRequest request, Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tenant);

        tenant.ContactEmail = Coalesce(request.ContactEmail, tenant.ContactEmail)?.ToLowerInvariant();
        tenant.ContactPhoneCountryCode = Coalesce(request.ContactPhoneCountryCode, tenant.ContactPhoneCountryCode);
        tenant.ContactPhone = Coalesce(request.ContactPhone, tenant.ContactPhone);

        tenant.AddressLine1 = Coalesce(request.AddressLine1, tenant.AddressLine1);
        tenant.AddressLine2 = Coalesce(request.AddressLine2, tenant.AddressLine2);
        tenant.City = Coalesce(request.City, tenant.City);
        tenant.State = Coalesce(request.State, tenant.State);
        tenant.Country = Coalesce(request.Country, tenant.Country);
        tenant.PostalCode = Coalesce(request.PostalCode, tenant.PostalCode);
    }

    public static OrganisationListItemResponse ToListItemResponse(
        this Tenant tenant, string rootDomain, int userCount, string? adminEmail) =>
        new(
            tenant.Id,
            tenant.Code,
            tenant.Name,
            tenant.Subdomain,
            $"{tenant.Subdomain}.{rootDomain}",
            tenant.Status,
            DescribeStatus(tenant.Status),
            tenant.LogoUrl,
            tenant.Country,
            userCount,
            adminEmail,
            tenant.CreatedAtUtc,
            tenant.UpdatedAtUtc,
            tenant.IsAwaitingReview,
            tenant.Version);

    public static OrganisationDetailResponse ToDetailResponse(
        this Tenant tenant,
        BusinessUnit businessUnit,
        int userCount,
        OrganisationAdminResponse? primaryAdmin,
        IReadOnlyList<TenantDomain> domains,
        IReadOnlyList<TenantDocument> documents,
        IReadOnlyList<TenantStatusHistory> timeline,
        DateTimeOffset asOf,
        bool includeInternalNotes)
    {
        var outstanding = OutstandingProfileFields(tenant);

        return new OrganisationDetailResponse(
            tenant.Id,
            tenant.BusinessUnitId,
            businessUnit.Name,
            tenant.Code,
            tenant.Name,
            tenant.LegalName,
            tenant.Subdomain,
            $"{tenant.Subdomain}.{businessUnit.RootDomain}",
            tenant.Status,
            DescribeStatus(tenant.Status),

            tenant.RegistrationNumber,
            tenant.TaxIdentificationNumber,
            tenant.PanNumber,
            tenant.GstNumber,
            tenant.OrganisationType,
            tenant.EstablishedOn,
            tenant.Description,
            tenant.WebsiteUrl,
            tenant.LogoUrl,

            tenant.ContactPersonName,
            tenant.ContactEmail,
            tenant.ContactPhoneCountryCode,
            tenant.ContactPhone,

            tenant.AddressLine1,
            tenant.AddressLine2,
            tenant.City,
            tenant.State,
            tenant.Country,
            tenant.PostalCode,

            tenant.TimeZone,
            tenant.DefaultCurrency,
            tenant.DefaultCulture,

            tenant.DefaultMfaRequirement,
            tenant.MaximumFailedAccessAttempts,
            tenant.LockoutDurationMinutes,
            tenant.PasswordMinimumLength,
            tenant.PasswordExpiryDays,
            tenant.SessionIdleTimeoutMinutes,
            tenant.MaximumUsers,
            userCount,

            tenant.InvitedAtUtc,
            tenant.InvitationAcceptedAtUtc,
            tenant.SubmittedAtUtc,
            tenant.ReviewStartedAtUtc,
            tenant.ApprovedAtUtc,
            tenant.RejectedAtUtc,
            tenant.RejectionReason,
            tenant.ActivatedAtUtc,
            tenant.SuspendedAtUtc,
            tenant.SuspensionReason,
            tenant.ResubmissionCount,

            tenant.CreatedAtUtc,
            tenant.CreatedByUserId,
            tenant.UpdatedAtUtc,
            tenant.UpdatedByUserId,
            tenant.Version,

            [.. domains.Select(ToDomainResponse)],
            [.. documents.Select(document => document.ToDocumentResponse(asOf))],
            [.. timeline.Select(history => history.ToTimelineResponse(includeInternalNotes))],
            primaryAdmin,

            PermittedActionsFor(tenant),
            outstanding,
            outstanding.Count == 0,
            EditableProfileFieldsFor(tenant));
    }

    public static OrganisationDomainResponse ToDomainResponse(this TenantDomain domain) =>
        new(
            domain.Id,
            domain.HostName,
            domain.DomainType,
            domain.IsPrimary,
            domain.IsVerified,
            domain.IsActive,
            domain.VerifiedAtUtc,
            // The token is only useful while it is still needed. Returning it after
            // verification is pointless exposure.
            domain.IsVerified ? null : domain.VerificationToken);

    public static OrganisationDocumentResponse ToDocumentResponse(
        this TenantDocument document, DateTimeOffset asOf) =>
        new(
            document.Id,
            document.DocumentType,
            SplitPascalCase(document.DocumentType.ToString()),
            document.FileName,
            document.ContentType,
            document.FileSizeBytes,
            document.Status,
            document.ReferenceNumber,
            document.IssuedOn,
            document.ExpiresOn,
            document.IsExpired(asOf),
            document.UploadedAtUtc,
            UploadedByName: null,
            document.ReviewedAtUtc,
            ReviewedByName: null,
            document.ReviewNotes,
            document.SubmissionId);

    /// <summary>
    /// One rung of the lifecycle ladder.
    /// </summary>
    /// <param name="history">The recorded transition.</param>
    /// <param name="includeInternalNotes">
    /// Whether the reviewer's private note may be included.
    ///
    /// FALSE ON EVERY TENANT-FACING READ, and this parameter exists because it was not. The
    /// rejection dialog offers a reviewer two separate fields: REASON, which is the message TO the
    /// organisation, and INTERNAL NOTE, which is the reviewer's own working note about them. The
    /// status banner correctly showed only the reason; the History tab beneath it rendered both,
    /// verbatim, to the organisation being reviewed - so a note reading "QA automation rejection,
    /// not a real compliance issue" was delivered to the customer it was written about.
    ///
    /// THE SPLIT IS ENFORCED HERE RATHER THAN IN THE ANGULAR TEMPLATE. A field the client is
    /// trusted not to render is a field that reaches the browser, sits in the network tab, and is
    /// one careless binding away from the screen.
    /// </param>
    public static OrganisationTimelineResponse ToTimelineResponse(
        this TenantStatusHistory history, bool includeInternalNotes) =>
        new(
            history.Id,
            history.FromStatus,
            history.ToStatus,
            DescribeStatus(history.ToStatus),
            history.OccurredAtUtc,
            history.ActorDisplayName,
            history.Reason,
            includeInternalNotes ? history.Notes : null);

    public static BusinessUnitResponse ToResponse(this BusinessUnit businessUnit, int tenantCount) =>
        new(
            businessUnit.Id,
            businessUnit.Code,
            businessUnit.Name,
            businessUnit.LegalName,
            businessUnit.RootDomain,
            businessUnit.Status,
            businessUnit.ContactEmail,
            businessUnit.ContactPhone,
            businessUnit.SupportEmail,
            businessUnit.LogoUrl,
            businessUnit.TimeZone,
            businessUnit.DefaultCurrency,
            businessUnit.DefaultCulture,
            businessUnit.MaximumTenants,
            tenantCount,
            businessUnit.Description,
            businessUnit.CreatedAtUtc,
            businessUnit.UpdatedAtUtc,
            businessUnit.Version);

    /// <summary>
    /// What the Organisation CURRENT STATE allows. Permission is a separate question, checked
    /// on each endpoint — this list is what the client uses to decide which buttons to draw,
    /// so nobody is offered an action that would answer 409.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(Tenant tenant) => tenant.Status switch
    {
        TenantStatus.Invited => ["View", "ResendInvitation", "Edit", "Archive"],
        TenantStatus.InvitationAccepted => ["View", "EditProfile", "Submit", "Archive"],
        TenantStatus.ProfileIncomplete => ["View", "EditProfile", "UploadDocument", "Submit", "Archive"],
        TenantStatus.Submitted => ["View", "StartReview", "Approve", "Reject"],
        TenantStatus.UnderReview => ["View", "Approve", "Reject"],
        TenantStatus.Rejected => ["View", "EditProfile", "UploadDocument", "Resubmit", "Archive"],
        TenantStatus.Resubmitted => ["View", "StartReview", "Approve", "Reject"],
        TenantStatus.Approved => ["View", "Activate", "Suspend", "Archive"],
        TenantStatus.Active => ["View", "Edit", "ManageDomains", "Suspend", "Archive"],
        TenantStatus.Suspended => ["View", "Reactivate", "Archive"],
        TenantStatus.Archived => ["View"],
        _ => ["View"]
    };

    /// <summary>
    /// Contact e-mail, telephone and postal address — the fields that stay editable for the
    /// life of the Organisation. See <see cref="ApplyContactAndAddress"/> for why these three
    /// and nothing else.
    /// </summary>
    public static readonly IReadOnlyList<string> ContactAndAddressFields =
    [
        nameof(UpdateOrganisationProfileRequest.ContactEmail),
        nameof(UpdateOrganisationProfileRequest.ContactPhoneCountryCode),
        nameof(UpdateOrganisationProfileRequest.ContactPhone),
        nameof(UpdateOrganisationProfileRequest.AddressLine1),
        nameof(UpdateOrganisationProfileRequest.AddressLine2),
        nameof(UpdateOrganisationProfileRequest.City),
        nameof(UpdateOrganisationProfileRequest.State),
        nameof(UpdateOrganisationProfileRequest.Country),
        nameof(UpdateOrganisationProfileRequest.PostalCode)
    ];

    /// <summary>Every profile field, for the states where the whole form is still open.</summary>
    public static readonly IReadOnlyList<string> AllProfileFields =
    [
        nameof(UpdateOrganisationProfileRequest.Name),
        nameof(UpdateOrganisationProfileRequest.LegalName),
        nameof(UpdateOrganisationProfileRequest.RegistrationNumber),
        nameof(UpdateOrganisationProfileRequest.TaxIdentificationNumber),
        nameof(UpdateOrganisationProfileRequest.PanNumber),
        nameof(UpdateOrganisationProfileRequest.GstNumber),
        nameof(UpdateOrganisationProfileRequest.OrganisationType),
        nameof(UpdateOrganisationProfileRequest.EstablishedOn),
        nameof(UpdateOrganisationProfileRequest.Description),
        nameof(UpdateOrganisationProfileRequest.WebsiteUrl),
        nameof(UpdateOrganisationProfileRequest.LogoUrl),
        nameof(UpdateOrganisationProfileRequest.ContactPersonName),
        .. ContactAndAddressFields,
        nameof(UpdateOrganisationProfileRequest.TimeZone),
        nameof(UpdateOrganisationProfileRequest.DefaultCurrency),
        nameof(UpdateOrganisationProfileRequest.DefaultCulture)
    ];

    /// <summary>
    /// True once a platform reviewer has accepted the identity fields, which is the point at
    /// which they stop being the Organisation's to change.
    ///
    /// SUSPENDED IS IN THE LIST even though no Edit action is offered there — a suspension is
    /// not a licence to rewrite the record, and the state check has to hold on its own rather
    /// than relying on the button being absent.
    /// </summary>
    public static bool IsProfileVerified(TenantStatus status) =>
        status is TenantStatus.Approved or TenantStatus.Active or TenantStatus.Suspended;

    /// <summary>
    /// Which profile fields this Organisation may still change, in its current state.
    ///
    /// SENT TO THE CLIENT for the same reason <see cref="PermittedActionsFor"/> is: the fields
    /// drawn as editable and the fields the API will actually accept cannot drift apart if only
    /// one side decides. Enforcement is still server-side — this list only spares somebody
    /// typing into a box whose value is going to be discarded.
    /// </summary>
    public static IReadOnlyList<string> EditableProfileFieldsFor(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (tenant.Status is TenantStatus.Submitted or TenantStatus.UnderReview
            or TenantStatus.Resubmitted or TenantStatus.Archived)
        {
            return [];
        }

        return IsProfileVerified(tenant.Status) ? ContactAndAddressFields : AllProfileFields;
    }

    /// <summary>
    /// The fields a profile still needs before it can be submitted.
    ///
    /// Returned rather than merely counted, so the screen can point at the specific gaps.
    /// "Profile incomplete" with no indication of what is missing is a guessing game.
    /// </summary>
    public static IReadOnlyList<string> OutstandingProfileFields(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var missing = new List<string>();

        void Require(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(field);
            }
        }

        Require(tenant.Name, nameof(Tenant.Name));
        Require(tenant.LegalName, nameof(Tenant.LegalName));
        Require(tenant.RegistrationNumber, nameof(Tenant.RegistrationNumber));
        Require(tenant.OrganisationType, nameof(Tenant.OrganisationType));
        Require(tenant.ContactPersonName, nameof(Tenant.ContactPersonName));
        Require(tenant.ContactEmail, nameof(Tenant.ContactEmail));
        Require(tenant.ContactPhone, nameof(Tenant.ContactPhone));
        Require(tenant.AddressLine1, nameof(Tenant.AddressLine1));
        Require(tenant.City, nameof(Tenant.City));
        Require(tenant.State, nameof(Tenant.State));
        Require(tenant.Country, nameof(Tenant.Country));
        Require(tenant.PostalCode, nameof(Tenant.PostalCode));

        return missing;
    }

    /// <summary>Human wording for a lifecycle status, so the client does not hard-code eleven strings.</summary>
    public static string DescribeStatus(TenantStatus status) => status switch
    {
        TenantStatus.Invited => "Invitation sent",
        TenantStatus.InvitationAccepted => "Invitation accepted",
        TenantStatus.ProfileIncomplete => "Profile incomplete",
        TenantStatus.Submitted => "Submitted for approval",
        TenantStatus.UnderReview => "Under review",
        TenantStatus.Rejected => "Rejected",
        TenantStatus.Resubmitted => "Resubmitted",
        TenantStatus.Approved => "Approved",
        TenantStatus.Active => "Active",
        TenantStatus.Suspended => "Suspended",
        TenantStatus.Archived => "Archived",
        _ => status.ToString()
    };

    private static string? Coalesce(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming.Trim();

    /// <summary>"RegistrationCertificate" becomes "Registration certificate".</summary>
    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);
        builder.Append(value[0]);

        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(value[index]));
            }
            else
            {
                builder.Append(value[index]);
            }
        }

        return builder.ToString();
    }
}
