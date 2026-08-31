using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.Configurations;

/// <summary>
/// Table don_donors. This is the mapping from section 9 of the developer contract; the column
/// names come out snake_case automatically because the context uses the naming convention.
/// </summary>
public sealed class DonorConfiguration : IEntityTypeConfiguration<Donor>
{
    public void Configure(EntityTypeBuilder<Donor> builder)
    {
        builder.ToTable("don_donors");
        builder.HasKey(donor => donor.Id);

        // Concurrency: the UPDATE statement carries id AND version, so a stale write matches
        // zero rows and EF raises DbUpdateConcurrencyException. That becomes CONCURRENCY_CONFLICT.
        builder.Property(donor => donor.Version).IsConcurrencyToken();

        builder.Property(donor => donor.DonorNumber).IsRequired();
        builder.Property(donor => donor.DonorType).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(donor => donor.FirstName);
        builder.Property(donor => donor.LastName);
        builder.Property(donor => donor.OrganisationName);
        builder.Property(donor => donor.PrimaryEmail);
        builder.Property(donor => donor.PrimaryPhone);
        builder.Property(donor => donor.PreferredLanguage).IsRequired();
        builder.Property(donor => donor.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(donor => donor.DoNotContact).IsRequired();

        builder.Property(donor => donor.ApprovalState).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(donor => donor.RelationshipOwnerName).HasMaxLength(200);
        builder.Property(donor => donor.NormalizedBusinessKey).HasMaxLength(320).IsRequired();
        builder.Property(donor => donor.CancellationReason).HasMaxLength(2000);
        builder.Property(donor => donor.ArchiveReason).HasMaxLength(2000);
        builder.Property(donor => donor.Notes).HasMaxLength(2000);

        // Natural key, scoped by organisation. Two organisations may both have DON-2026-000001.
        builder.HasIndex(donor => new { donor.OrganisationId, donor.DonorNumber })
            .IsUnique()
            .HasDatabaseName("ix_don_donors_org_donor_number");

        // Duplicate detection. Not unique: two records CAN share a key until a steward decides,
        // and refusing the second one at the database would hide the duplicate rather than
        // surface it for review.
        builder.HasIndex(donor => new { donor.OrganisationId, donor.NormalizedBusinessKey })
            .HasDatabaseName("ix_don_donors_org_business_key");

        // Search index from section 9: "status, updated_at DESC". The descending half matters —
        // the donor list is ordered newest-first, and an ascending index cannot serve that
        // ordering without a sort step on top.
        builder.HasIndex(donor => new { donor.Status, donor.UpdatedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_don_donors_status_updated");

        builder.HasIndex(donor => donor.RelationshipOwnerUserId)
            .HasDatabaseName("ix_don_donors_owner");

        builder.HasMany(donor => donor.Contacts)
            .WithOne(contact => contact.Donor)
            .HasForeignKey(contact => contact.DonorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(donor => donor.Tags)
            .WithOne(tag => tag.Donor)
            .HasForeignKey(tag => tag.DonorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Consents and interactions are audited history, so RESTRICT: a donor that has them
        // cannot be deleted at all, which is what section 9 asks for.
        builder.HasMany(donor => donor.Consents)
            .WithOne(consent => consent.Donor)
            .HasForeignKey(consent => consent.DonorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(donor => donor.Interactions)
            .WithOne(interaction => interaction.Donor)
            .HasForeignKey(interaction => interaction.DonorId)
            .OnDelete(DeleteBehavior.Restrict);

        // DisplayName is computed in C# from the type and the name parts, so EF must not try
        // to map it to a column.
        builder.Ignore(donor => donor.DisplayName);
    }
}

/// <summary>Table don_donor_contacts.</summary>
public sealed class DonorContactConfiguration : IEntityTypeConfiguration<DonorContact>
{
    public void Configure(EntityTypeBuilder<DonorContact> builder)
    {
        builder.ToTable("don_donor_contacts");
        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.Version).IsConcurrencyToken();

        builder.Property(contact => contact.Name).HasMaxLength(160).IsRequired();
        builder.Property(contact => contact.Description).HasMaxLength(2000);
        builder.Property(contact => contact.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(contact => contact.Channel).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(contact => contact.Value).HasMaxLength(320).IsRequired();

        builder.HasIndex(contact => contact.DonorId).HasDatabaseName("ix_don_donor_contacts_donor");
    }
}

/// <summary>Table don_donor_tags.</summary>
public sealed class DonorTagConfiguration : IEntityTypeConfiguration<DonorTag>
{
    public void Configure(EntityTypeBuilder<DonorTag> builder)
    {
        builder.ToTable("don_donor_tags");
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Version).IsConcurrencyToken();

        builder.Property(tag => tag.Name).HasMaxLength(160).IsRequired();
        builder.Property(tag => tag.Description).HasMaxLength(2000);
        builder.Property(tag => tag.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(tag => tag.Code).HasMaxLength(60).IsRequired();

        // A tag cannot be attached to the same donor twice.
        builder.HasIndex(tag => new { tag.DonorId, tag.Code })
            .IsUnique()
            .HasDatabaseName("ix_don_donor_tags_donor_code");
    }
}

/// <summary>Table don_donor_interactions.</summary>
public sealed class DonorInteractionConfiguration : IEntityTypeConfiguration<DonorInteraction>
{
    public void Configure(EntityTypeBuilder<DonorInteraction> builder)
    {
        builder.ToTable("don_donor_interactions");
        builder.HasKey(interaction => interaction.Id);
        builder.Property(interaction => interaction.Version).IsConcurrencyToken();

        builder.Property(interaction => interaction.Name).HasMaxLength(160).IsRequired();
        builder.Property(interaction => interaction.Description).HasMaxLength(2000);
        builder.Property(interaction => interaction.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(interaction => interaction.InteractionType).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(interaction => interaction.Channel).HasConversion<string>().HasMaxLength(80);
        builder.Property(interaction => interaction.Outcome).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(interaction => interaction.PerformedByName).HasMaxLength(200);

        builder.HasIndex(interaction => new { interaction.DonorId, interaction.OccurredAtUtc })
            .HasDatabaseName("ix_don_donor_interactions_donor_occurred");

        builder.HasIndex(interaction => interaction.LeadId)
            .HasDatabaseName("ix_don_donor_interactions_lead");
    }
}

/// <summary>Table don_donor_promises.</summary>
public sealed class DonorPromiseConfiguration : IEntityTypeConfiguration<DonorPromise>
{
    public void Configure(EntityTypeBuilder<DonorPromise> builder)
    {
        builder.ToTable("don_donor_promises");
        builder.HasKey(promise => promise.Id);
        builder.Property(promise => promise.Version).IsConcurrencyToken();

        builder.Property(promise => promise.Reference).HasMaxLength(40).IsRequired();
        // Money is decimal(18,2), never double. A float would quietly lose paise.
        builder.Property(promise => promise.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(promise => promise.Currency).HasMaxLength(3).IsRequired();
        builder.Property(promise => promise.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(promise => promise.Notes).HasMaxLength(2000);

        builder.HasIndex(promise => promise.Reference).IsUnique().HasDatabaseName("ix_don_donor_promises_reference");
        builder.HasIndex(promise => promise.DonorId).HasDatabaseName("ix_don_donor_promises_donor");

        builder.HasOne(promise => promise.Donor)
            .WithMany()
            .HasForeignKey(promise => promise.DonorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(promise => promise.Campaign)
            .WithMany()
            .HasForeignKey(promise => promise.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Table don_donor_documents.</summary>
public sealed class DonorDocumentConfiguration : IEntityTypeConfiguration<DonorDocument>
{
    public void Configure(EntityTypeBuilder<DonorDocument> builder)
    {
        builder.ToTable("don_donor_documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Version).IsConcurrencyToken();

        builder.Property(document => document.Reference).HasMaxLength(300).IsRequired();
        builder.Property(document => document.Name).HasMaxLength(200).IsRequired();
        builder.Property(document => document.Description).HasMaxLength(2000);
        builder.Property(document => document.Classification).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(document => document.ContentType).HasMaxLength(120);
        builder.Property(document => document.ScanStatus).HasMaxLength(80);

        builder.HasIndex(document => document.DonorId).HasDatabaseName("ix_don_donor_documents_donor");

        builder.HasOne(document => document.Donor)
            .WithMany()
            .HasForeignKey(document => document.DonorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Table don_donor_donation_summaries.</summary>
public sealed class DonorDonationSummaryConfiguration : IEntityTypeConfiguration<DonorDonationSummary>
{
    public void Configure(EntityTypeBuilder<DonorDonationSummary> builder)
    {
        builder.ToTable("don_donor_donation_summaries");
        builder.HasKey(summary => summary.Id);
        builder.Property(summary => summary.Version).IsConcurrencyToken();

        builder.Property(summary => summary.Stage).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(summary => summary.Currency).HasMaxLength(3).IsRequired();
        builder.Property(summary => summary.TotalAmount).HasPrecision(18, 2).IsRequired();

        // One row per donor, stage and currency: the projection is a total, not a log.
        builder.HasIndex(summary => new { summary.DonorId, summary.Stage, summary.Currency })
            .IsUnique()
            .HasDatabaseName("ix_don_donation_summaries_donor_stage");

        builder.HasOne(summary => summary.Donor)
            .WithMany()
            .HasForeignKey(summary => summary.DonorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
