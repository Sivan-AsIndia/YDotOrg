using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>The campaign readiness checklist and the blockers raised against it.</summary>
public sealed class CampaignReadinessCheckConfiguration
    : IEntityTypeConfiguration<CampaignReadinessCheck>
{
    public void Configure(EntityTypeBuilder<CampaignReadinessCheck> builder)
    {
        builder.ToTable("cam_campaign_readiness_checks");

        builder.HasKey(check => check.Id);

        // One check name per campaign. Two checks called "Payment configured" make "has the
        // payment check passed?" a question with two answers.
        builder.HasIndex(check => new { check.CampaignId, check.CheckName })
            .HasDatabaseName("ix_cam_readiness_checks_campaign_name")
            .IsUnique();

        // The index behind the launch gate: "any required check on this campaign not passed?".
        builder.HasIndex(check => new { check.CampaignId, check.RequiredForLaunch, check.Status })
            .HasDatabaseName("ix_cam_readiness_checks_launch_gate");

        builder.Property(check => check.CheckName).HasMaxLength(200).IsRequired();
        builder.Property(check => check.Description).HasMaxLength(1000);
        builder.Property(check => check.SuccessCriteria).HasMaxLength(1000).IsRequired();
        builder.Property(check => check.Notes).HasMaxLength(2000);

        builder.Property(check => check.Category)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(check => check.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(check => check.Version).IsConcurrencyToken();

        builder.HasMany(check => check.Blockers)
            .WithOne(blocker => blocker.ReadinessCheck)
            .HasForeignKey(blocker => blocker.CampaignReadinessCheckId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Something standing in the way of a readiness check passing.</summary>
public sealed class CampaignReadinessBlockerConfiguration
    : IEntityTypeConfiguration<CampaignReadinessBlocker>
{
    public void Configure(EntityTypeBuilder<CampaignReadinessBlocker> builder)
    {
        builder.ToTable("cam_campaign_readiness_blockers");

        builder.HasKey(blocker => blocker.Id);

        // "What is on my plate?" - the query behind a person's blocker queue.
        builder.HasIndex(blocker => new { blocker.OwnerUserId, blocker.IsResolved })
            .HasDatabaseName("ix_cam_readiness_blockers_owner");

        // AT MOST ONE OPEN BLOCKER PER CHECK, enforced by a filtered unique index and not only
        // by the handler. The handler check is a read followed by a write, so two simultaneous
        // requests can both read "none open" and both insert; this is what stops that race.
        builder.HasIndex(blocker => blocker.CampaignReadinessCheckId)
            .HasDatabaseName("ix_cam_readiness_blockers_open_unique")
            .IsUnique()
            .HasFilter("is_resolved = false");

        builder.Property(blocker => blocker.BlockerNote).HasMaxLength(2000).IsRequired();
        builder.Property(blocker => blocker.ResolutionNote).HasMaxLength(2000);

        builder.Property(blocker => blocker.Version).IsConcurrencyToken();

        // A resolved blocker records who resolved it and when. An unresolved one records
        // neither. Both halves are enforced, so a half-written resolution cannot exist.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_readiness_blockers_resolution",
            "(is_resolved = false AND resolved_at_utc IS NULL AND resolved_by_user_id IS NULL) "
            + "OR (is_resolved = true AND resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)"));
    }
}
