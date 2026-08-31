using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// The tenancy root: BusinessUnit, Tenant, and the satellites that make an Organisation
/// reachable and reviewable.
///
/// NOTE WHAT IS ABSENT FROM THESE FOUR TABLES: a global query filter. They are the isolation
/// boundary rather than something inside it, so filtering them by the current Organisation
/// would make an Organisation unable to load itself. What protects them instead is the
/// platform permission set on the endpoints that read them.
/// </summary>
public sealed class BusinessUnitConfiguration : IEntityTypeConfiguration<BusinessUnit>
{
    public void Configure(EntityTypeBuilder<BusinessUnit> builder)
    {
        builder.ToTable("iam_business_units");

        builder.HasKey(unit => unit.Id);

        builder.HasIndex(unit => unit.Code)
            .HasDatabaseName("ix_iam_business_units_code")
            .IsUnique();

        // A host can only belong to one BusinessUnit, or host resolution becomes ambiguous.
        builder.HasIndex(unit => unit.RootDomain)
            .HasDatabaseName("ix_iam_business_units_root_domain")
            .IsUnique();

        builder.Property(unit => unit.Code).HasMaxLength(50).IsRequired();
        builder.Property(unit => unit.Name).HasMaxLength(200).IsRequired();
        builder.Property(unit => unit.LegalName).HasMaxLength(250);
        builder.Property(unit => unit.RootDomain).HasMaxLength(253).IsRequired();
        builder.Property(unit => unit.ContactEmail).HasMaxLength(320);
        builder.Property(unit => unit.ContactPhone).HasMaxLength(30);
        builder.Property(unit => unit.SupportEmail).HasMaxLength(320);
        builder.Property(unit => unit.LogoUrl).HasMaxLength(500);
        builder.Property(unit => unit.TimeZone).HasMaxLength(80).IsRequired();
        builder.Property(unit => unit.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(unit => unit.DefaultCulture).HasMaxLength(20).IsRequired();
        builder.Property(unit => unit.Description).HasMaxLength(2000);

        builder.Property(unit => unit.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(unit => unit.Version).IsConcurrencyToken();
    }
}

/// <summary>The Organisation. Called Tenant here, Organisation everywhere a person can see.</summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("iam_tenants");

        builder.HasKey(tenant => tenant.Id);

        // Both scoped to the BusinessUnit, because two BusinessUnits could legitimately each
        // have a TEN001 - they are separate platforms sharing a database.
        builder.HasIndex(tenant => new { tenant.BusinessUnitId, tenant.Code })
            .HasDatabaseName("ix_iam_tenants_business_unit_code")
            .IsUnique();

        builder.HasIndex(tenant => new { tenant.BusinessUnitId, tenant.Subdomain })
            .HasDatabaseName("ix_iam_tenants_business_unit_subdomain")
            .IsUnique();

        builder.HasIndex(tenant => tenant.Status)
            .HasDatabaseName("ix_iam_tenants_status");

        builder.Property(tenant => tenant.Code).HasMaxLength(50).IsRequired();
        builder.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.LegalName).HasMaxLength(250);
        builder.Property(tenant => tenant.Subdomain).HasMaxLength(63).IsRequired();

        builder.Property(tenant => tenant.RegistrationNumber).HasMaxLength(100);
        builder.Property(tenant => tenant.TaxIdentificationNumber).HasMaxLength(100);
        builder.Property(tenant => tenant.PanNumber).HasMaxLength(20);
        builder.Property(tenant => tenant.GstNumber).HasMaxLength(30);
        builder.Property(tenant => tenant.OrganisationType).HasMaxLength(100);
        builder.Property(tenant => tenant.Description).HasMaxLength(2000);
        builder.Property(tenant => tenant.WebsiteUrl).HasMaxLength(500);
        builder.Property(tenant => tenant.LogoUrl).HasMaxLength(500);

        builder.Property(tenant => tenant.ContactPersonName).HasMaxLength(200);
        builder.Property(tenant => tenant.ContactEmail).HasMaxLength(320);
        builder.Property(tenant => tenant.ContactPhoneCountryCode).HasMaxLength(8);
        builder.Property(tenant => tenant.ContactPhone).HasMaxLength(20);

        builder.Property(tenant => tenant.AddressLine1).HasMaxLength(250);
        builder.Property(tenant => tenant.AddressLine2).HasMaxLength(250);
        builder.Property(tenant => tenant.City).HasMaxLength(120);
        builder.Property(tenant => tenant.State).HasMaxLength(120);
        builder.Property(tenant => tenant.Country).HasMaxLength(120);
        builder.Property(tenant => tenant.PostalCode).HasMaxLength(20);

        builder.Property(tenant => tenant.TimeZone).HasMaxLength(80).IsRequired();
        builder.Property(tenant => tenant.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(tenant => tenant.DefaultCulture).HasMaxLength(20).IsRequired();

        builder.Property(tenant => tenant.RejectionReason).HasMaxLength(2000);
        builder.Property(tenant => tenant.SuspensionReason).HasMaxLength(2000);

        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(tenant => tenant.DefaultMfaRequirement).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(tenant => tenant.Version).IsConcurrencyToken();

        builder.HasOne(tenant => tenant.BusinessUnit)
            .WithMany(unit => unit.Tenants)
            .HasForeignKey(tenant => tenant.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            // A rejection the TenantAdmin cannot act on is a dead end, so the database
            // insists a Rejected Organisation carries a reason.
            table.HasCheckConstraint(
                "ck_iam_tenants_rejection_has_reason",
                "status <> 'Rejected' OR rejection_reason IS NOT NULL");

            table.HasCheckConstraint(
                "ck_iam_tenants_suspension_has_reason",
                "status <> 'Suspended' OR suspension_reason IS NOT NULL");

            // The lockout policy has to be usable. Zero attempts would lock everybody out on
            // their first typo; a zero-minute lockout would not be a lockout at all.
            table.HasCheckConstraint(
                "ck_iam_tenants_lockout_policy",
                "maximum_failed_access_attempts >= 1 AND lockout_duration_minutes >= 1");

            table.HasCheckConstraint(
                "ck_iam_tenants_password_length",
                "password_minimum_length >= 6 AND password_minimum_length <= 128");
        });
    }
}

/// <summary>
/// The host-to-Organisation mapping, and the single most security-sensitive table in the
/// schema: it is what an anonymous sign-in is resolved through.
/// </summary>
public sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("iam_tenant_domains");

        builder.HasKey(domain => domain.Id);

        // UNIQUE PLATFORM-WIDE, and this is the important one. A host that resolved to two
        // Organisations would mean credentials being checked against whichever row came back
        // first. The unique index makes that impossible rather than unlikely.
        builder.HasIndex(domain => domain.HostName)
            .HasDatabaseName("ix_iam_tenant_domains_host")
            .IsUnique();

        builder.HasIndex(domain => domain.TenantId)
            .HasDatabaseName("ix_iam_tenant_domains_tenant");

        // Exactly one primary host per Organisation, so link-building is never ambiguous.
        builder.HasIndex(domain => domain.TenantId)
            .HasDatabaseName("ix_iam_tenant_domains_primary")
            .IsUnique()
            .HasFilter("is_primary = TRUE");

        builder.Property(domain => domain.HostName).HasMaxLength(253).IsRequired();
        builder.Property(domain => domain.VerificationToken).HasMaxLength(200);
        builder.Property(domain => domain.DomainType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(domain => domain.Version).IsConcurrencyToken();

        builder.HasOne(domain => domain.Tenant)
            .WithMany(tenant => tenant.Domains)
            .HasForeignKey(domain => domain.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// A grouped document submission — the unit a reviewer approves or sends back.
/// Tenant-owned, so filtered.
/// </summary>
public sealed class TenantDocumentSubmissionConfiguration
    : IEntityTypeConfiguration<TenantDocumentSubmission>
{
    public void Configure(EntityTypeBuilder<TenantDocumentSubmission> builder)
    {
        builder.ToTable("iam_tenant_document_submissions");

        builder.HasKey(submission => submission.Id);

        // The review queue reads "everything awaiting a decision, oldest first", so status
        // leads the index and the submission time follows it.
        builder.HasIndex(submission => new { submission.Status, submission.SubmittedAtUtc })
            .HasDatabaseName("ix_iam_tenant_document_submissions_status_submitted");

        builder.HasIndex(submission => new { submission.TenantId, submission.DocumentType })
            .HasDatabaseName("ix_iam_tenant_document_submissions_tenant_type");

        builder.Property(submission => submission.Title).HasMaxLength(200);
        builder.Property(submission => submission.Notes).HasMaxLength(2000);
        builder.Property(submission => submission.DecisionNotes).HasMaxLength(2000);

        builder.Property(submission => submission.DocumentType)
            .HasConversion<string>().HasMaxLength(60).IsRequired();

        builder.Property(submission => submission.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(submission => submission.Version).IsConcurrencyToken();

        builder.HasOne(submission => submission.Tenant)
            .WithMany(tenant => tenant.DocumentSubmissions)
            .HasForeignKey(submission => submission.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // A refusal or a send-back without a reason is a dead end rather than a decision, and
        // the database is the last place that can still say so.
        builder.ToTable(table =>
            table.HasCheckConstraint(
                "ck_iam_tenant_document_submissions_decision_has_notes",
                "status NOT IN ('Rejected', 'ReuploadRequested') OR decision_notes IS NOT NULL"));
    }
}

/// <summary>Documents uploaded during onboarding. Tenant-owned, so filtered.</summary>
public sealed class TenantDocumentConfiguration : IEntityTypeConfiguration<TenantDocument>
{
    public void Configure(EntityTypeBuilder<TenantDocument> builder)
    {
        builder.ToTable("iam_tenant_documents");

        builder.HasKey(document => document.Id);

        builder.HasIndex(document => new { document.TenantId, document.DocumentType })
            .HasDatabaseName("ix_iam_tenant_documents_tenant_type");

        builder.Property(document => document.FileName).HasMaxLength(260).IsRequired();
        builder.Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(document => document.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(document => document.ContentHash).HasMaxLength(128);
        builder.Property(document => document.StorageVersionId).HasMaxLength(200);
        builder.Property(document => document.ReviewNotes).HasMaxLength(2000);
        builder.Property(document => document.ReferenceNumber).HasMaxLength(100);

        builder.Property(document => document.DocumentType).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(document => document.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(document => document.Version).IsConcurrencyToken();

        builder.HasOne(document => document.Tenant)
            .WithMany(tenant => tenant.Documents)
            .HasForeignKey(document => document.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade: deleting a submission takes its files with it, because a file with no
        // submission is unreachable from every screen and reviewable by nobody.
        builder.HasOne(document => document.Submission)
            .WithMany(submission => submission.Documents)
            .HasForeignKey(document => document.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
            table.HasCheckConstraint(
                "ck_iam_tenant_documents_rejection_has_notes",
                "status <> 'Rejected' OR review_notes IS NOT NULL"));
    }
}

/// <summary>
/// The Organisation lifecycle ladder. Append-only: there is no update path, so the timeline
/// cannot be rewritten after the fact.
/// </summary>
public sealed class TenantStatusHistoryConfiguration : IEntityTypeConfiguration<TenantStatusHistory>
{
    public void Configure(EntityTypeBuilder<TenantStatusHistory> builder)
    {
        builder.ToTable("iam_tenant_status_history");

        builder.HasKey(history => history.Id);

        // Descending, because the timeline is always read newest first.
        builder.HasIndex(history => new { history.TenantId, history.OccurredAtUtc })
            .HasDatabaseName("ix_iam_tenant_status_history_tenant_time")
            .IsDescending(false, true);

        builder.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(40);
        builder.Property(history => history.ToStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(history => history.ActorDisplayName).HasMaxLength(160);
        builder.Property(history => history.Reason).HasMaxLength(2000);
        builder.Property(history => history.Notes).HasMaxLength(2000);
        builder.Property(history => history.CorrelationId).HasMaxLength(80);
        builder.Property(history => history.Version).IsConcurrencyToken();

        builder.HasOne(history => history.Tenant)
            .WithMany(tenant => tenant.StatusHistory)
            .HasForeignKey(history => history.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
