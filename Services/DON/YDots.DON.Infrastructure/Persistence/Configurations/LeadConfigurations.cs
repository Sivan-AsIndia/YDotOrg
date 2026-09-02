using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.Configurations;

/// <summary>Table don_campaigns.</summary>
public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("don_campaigns");
        builder.HasKey(campaign => campaign.Id);
        builder.Property(campaign => campaign.Version).IsConcurrencyToken();

        builder.Property(campaign => campaign.Code).HasMaxLength(60).IsRequired();
        builder.Property(campaign => campaign.Name).HasMaxLength(200).IsRequired();
        builder.Property(campaign => campaign.Description).HasMaxLength(2000);
        builder.Property(campaign => campaign.Status).HasConversion<string>().HasMaxLength(80).IsRequired();

        builder.HasIndex(campaign => new { campaign.OrganisationId, campaign.Code })
            .IsUnique()
            .HasDatabaseName("ix_don_campaigns_org_code");
    }
}

/// <summary>Table don_leads.</summary>
public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("don_leads");
        builder.HasKey(lead => lead.Id);
        builder.Property(lead => lead.Version).IsConcurrencyToken();

        builder.Property(lead => lead.LeadReference).HasMaxLength(40).IsRequired();
        builder.Property(lead => lead.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(lead => lead.LastName).HasMaxLength(100);
        builder.Property(lead => lead.MobileNumber).HasMaxLength(30);
        builder.Property(lead => lead.EmailAddress).HasMaxLength(320);
        builder.Property(lead => lead.PreferredLanguage).HasMaxLength(20).IsRequired();
        builder.Property(lead => lead.City).HasMaxLength(150);
        builder.Property(lead => lead.GeographyCode).HasMaxLength(60);
        builder.Property(lead => lead.Source).HasMaxLength(200).IsRequired();
        builder.Property(lead => lead.ConsentState).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(lead => lead.ConsentEvidenceReference).HasMaxLength(300);
        builder.Property(lead => lead.Notes).HasMaxLength(2000);
        builder.Property(lead => lead.DuplicateCandidateSummary).HasMaxLength(1000);
        builder.Property(lead => lead.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(lead => lead.OwnerName).HasMaxLength(200);
        builder.Property(lead => lead.TeamCode).HasMaxLength(60);
        builder.Property(lead => lead.NextAction).HasMaxLength(300);
        builder.Property(lead => lead.SlaState).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(lead => lead.LastContactOutcome).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(lead => lead.ClosureReason).HasMaxLength(2000);

        // Stored as strings like every other enum here, so a value is readable in the database and
        // a new member never renumbers an existing row.
        builder.Property(lead => lead.Temperature).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(lead => lead.DonationPotential).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.HasIndex(lead => new { lead.OrganisationId, lead.LeadReference })
            .IsUnique()
            .HasDatabaseName("ix_don_leads_org_reference");

        // The work queue sorts by due date inside a status, so the index carries both.
        builder.HasIndex(lead => new { lead.Status, lead.NextActionDueUtc })
            .HasDatabaseName("ix_don_leads_status_due");

        builder.HasIndex(lead => lead.OwnerUserId).HasDatabaseName("ix_don_leads_owner");
        builder.HasIndex(lead => lead.CampaignId).HasDatabaseName("ix_don_leads_campaign");
        builder.HasIndex(lead => lead.EmailAddress).HasDatabaseName("ix_don_leads_email");
        builder.HasIndex(lead => lead.MobileNumber).HasDatabaseName("ix_don_leads_mobile");

        // RESTRICT: a campaign with leads on it is history and cannot be deleted out from under them.
        builder.HasOne(lead => lead.Campaign)
            .WithMany(campaign => campaign.Leads)
            .HasForeignKey(lead => lead.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        // CASCADE is safe here and only here: an assignment row is meaningless without its lead,
        // and a lead can only be deleted at all while it is an unused draft with no assignments.
        builder.HasMany(lead => lead.Assignments)
            .WithOne(assignment => assignment.Lead)
            .HasForeignKey(assignment => assignment.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table don_lead_assignments.</summary>
public sealed class LeadAssignmentConfiguration : IEntityTypeConfiguration<LeadAssignment>
{
    public void Configure(EntityTypeBuilder<LeadAssignment> builder)
    {
        builder.ToTable("don_lead_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Version).IsConcurrencyToken();

        builder.Property(assignment => assignment.PreviousOwnerName).HasMaxLength(200);
        builder.Property(assignment => assignment.NewOwnerName).HasMaxLength(200).IsRequired();
        builder.Property(assignment => assignment.AssignmentReason).HasMaxLength(2000).IsRequired();

        builder.HasIndex(assignment => new { assignment.LeadId, assignment.EffectiveAtUtc })
            .HasDatabaseName("ix_don_lead_assignments_lead_effective");

        builder.HasIndex(assignment => assignment.NewOwnerUserId)
            .HasDatabaseName("ix_don_lead_assignments_owner");
    }
}
