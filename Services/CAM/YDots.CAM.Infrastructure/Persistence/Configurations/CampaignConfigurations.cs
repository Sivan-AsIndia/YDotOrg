using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// The campaign aggregate: the campaign itself, its owners, its channels and its lifecycle
/// actions.
///
/// EVERY UNIQUE INDEX ON AN ORGANISATION-OWNED TABLE INCLUDES <c>tenant_id</c>. That is what
/// lets two Organisations each run a campaign coded SUMMER25 while neither can run two - and
/// the reason the old global unique index on Code was wrong: the first Organisation to use a
/// code would have taken it away from everybody else on the platform.
/// </summary>
public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("cam_campaigns");

        builder.HasKey(campaign => campaign.Id);

        // Unique inside the Organisation, not across the platform. See the class comment.
        builder.HasIndex(campaign => new { campaign.TenantId, campaign.Code })
            .HasDatabaseName("ix_cam_campaigns_tenant_code")
            .IsUnique();

        // The register's default ordering, and the index behind its status tiles.
        builder.HasIndex(campaign => new { campaign.TenantId, campaign.Status, campaign.StartDate })
            .HasDatabaseName("ix_cam_campaigns_tenant_status_start");

        builder.Property(campaign => campaign.Code).HasMaxLength(20).IsRequired();
        builder.Property(campaign => campaign.Name).HasMaxLength(250).IsRequired();
        builder.Property(campaign => campaign.Purpose)
            .HasMaxLength(CampaignFieldLimits.Purpose).IsRequired();
        builder.Property(campaign => campaign.FundOrProgramme).HasMaxLength(250).IsRequired();
        builder.Property(campaign => campaign.ZipCode).HasMaxLength(20);
        // THE TWO RICH-TEXT COLUMNS HOLD MARKUP, so they are sized for the editor's HTML
        // rather than for the number of characters the wizard's counter shows. See
        // CampaignFieldLimits - a public description that read as 770 characters on screen
        // arrived here as over a thousand characters of tags and would not save.
        builder.Property(campaign => campaign.PublicDescription)
            .HasMaxLength(CampaignFieldLimits.PublicDescription);
        builder.Property(campaign => campaign.TermsAndNotice)
            .HasMaxLength(CampaignFieldLimits.TermsAndNotice);

        // MONEY GETS EXPLICIT PRECISION. Without it Npgsql picks a default that silently
        // truncates, and a target of 1,234,567.89 becoming 1,234,568 is not something anybody
        // notices until a report disagrees with a bank statement.
        builder.Property(campaign => campaign.TargetAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(campaign => campaign.BudgetAmount).HasPrecision(18, 2);

        // Enums as TEXT, matching IAM and DON. A status reads as "Active" in a database console
        // rather than as a 5 nobody can decode.
        builder.Property(campaign => campaign.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(campaign => campaign.LifecycleActivation)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(campaign => campaign.Version).IsConcurrencyToken();

        builder.HasMany(campaign => campaign.Owners)
            .WithOne(owner => owner.Campaign)
            .HasForeignKey(owner => owner.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(campaign => campaign.Channels)
            .WithOne(channel => channel.Campaign)
            .HasForeignKey(channel => channel.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(campaign => campaign.LifecycleActions)
            .WithOne(action => action.Campaign)
            .HasForeignKey(action => action.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT for tracking assets and readiness checks, unlike the cascades above. Owners
        // and channels are part of the campaign; a tracking asset has a reference that may
        // already be printed on a poster, and a readiness check is evidence of what was verified
        // before launch. Neither should disappear because somebody deleted a campaign.
        builder.HasMany(campaign => campaign.TrackingAssets)
            .WithOne(asset => asset.Campaign)
            .HasForeignKey(asset => asset.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(campaign => campaign.ReadinessChecks)
            .WithOne(check => check.Campaign)
            .HasForeignKey(check => check.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_cam_campaigns_dates", "end_date >= start_date");

            table.HasCheckConstraint(
                "ck_cam_campaigns_target", "target_amount >= 0");

            table.HasCheckConstraint(
                "ck_cam_campaigns_budget", "budget_amount IS NULL OR budget_amount >= 0");

            table.HasCheckConstraint(
                "ck_cam_campaigns_days_before_start", "days_before_start >= 0");
        });
    }
}

/// <summary>One person accountable for a campaign.</summary>
public sealed class CampaignOwnerConfiguration : IEntityTypeConfiguration<CampaignOwner>
{
    public void Configure(EntityTypeBuilder<CampaignOwner> builder)
    {
        builder.ToTable("cam_campaign_owners");

        builder.HasKey(owner => owner.Id);

        // One person owns a campaign at most once. Without this the same owner could be added
        // twice and every notification would go out in duplicate.
        builder.HasIndex(owner => new { owner.CampaignId, owner.OwnerId })
            .HasDatabaseName("ix_cam_campaign_owners_unique")
            .IsUnique();

        // "Which campaigns do I own?" - the query behind the My Campaigns filter.
        builder.HasIndex(owner => owner.OwnerId)
            .HasDatabaseName("ix_cam_campaign_owners_owner");

        builder.Property(owner => owner.Version).IsConcurrencyToken();
    }
}

/// <summary>A channel a campaign runs on.</summary>
public sealed class CampaignChannelConfiguration : IEntityTypeConfiguration<CampaignChannel>
{
    public void Configure(EntityTypeBuilder<CampaignChannel> builder)
    {
        builder.ToTable("cam_campaign_channels");

        builder.HasKey(link => link.Id);

        builder.HasIndex(link => new { link.CampaignId, link.ChannelId })
            .HasDatabaseName("ix_cam_campaign_channels_unique")
            .IsUnique();

        // RESTRICT: a channel some campaign still uses must not be deletable, because doing so
        // would silently strip the channel off every campaign that named it.
        builder.HasOne(link => link.Channel)
            .WithMany()
            .HasForeignKey(link => link.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>One lifecycle transition requested against a campaign.</summary>
public sealed class CampaignLifecycleActionConfiguration : IEntityTypeConfiguration<CampaignLifecycleAction>
{
    public void Configure(EntityTypeBuilder<CampaignLifecycleAction> builder)
    {
        builder.ToTable("cam_campaign_lifecycle_actions");

        builder.HasKey(action => action.Id);

        builder.HasIndex(action => new { action.CampaignId, action.ActionType, action.ActionStatus })
            .HasDatabaseName("ix_cam_lifecycle_actions_campaign_type_status");

        builder.Property(action => action.ActionType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(action => action.ActionStatus)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(action => action.ReasonCategory).HasMaxLength(100);
        builder.Property(action => action.DetailedReason).HasMaxLength(2000);
        builder.Property(action => action.CommunicationImpact).HasMaxLength(2000);
        builder.Property(action => action.ClosureSummary).HasMaxLength(4000);

        builder.Property(action => action.Version).IsConcurrencyToken();

        // AT MOST ONE PENDING CLOSE REQUEST PER CAMPAIGN, enforced by a filtered unique index
        // rather than only by the handler check. The handler check is a read followed by a
        // write, so two simultaneous requests can both read "none pending" and both insert;
        // this is what actually stops that race.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_lifecycle_actions_approval",
            "approved_at_utc IS NULL OR approved_by_user_id IS NOT NULL"));

        builder.HasIndex(action => action.CampaignId)
            .HasDatabaseName("ix_cam_lifecycle_actions_pending_close")
            .IsUnique()
            .HasFilter("action_type = 'RequestClose' AND action_status = 'Pending'");
    }
}

/// <summary>One row of the append-only campaign audit trail.</summary>
public sealed class CampaignAuditEventConfiguration : IEntityTypeConfiguration<CampaignAuditEvent>
{
    public void Configure(EntityTypeBuilder<CampaignAuditEvent> builder)
    {
        builder.ToTable("cam_audit_events");

        builder.HasKey(audit => audit.Id);

        // The two questions the trail is asked: "what happened to this record?" and "what
        // happened in this Organisation recently?".
        builder.HasIndex(audit => new { audit.TargetType, audit.TargetId, audit.OccurredAtUtc })
            .HasDatabaseName("ix_cam_audit_events_target");

        builder.HasIndex(audit => new { audit.TenantId, audit.OccurredAtUtc })
            .HasDatabaseName("ix_cam_audit_events_tenant_time");

        builder.Property(audit => audit.ActionCode).HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.Reason).HasMaxLength(2000);
        builder.Property(audit => audit.IpAddress).HasMaxLength(64);
        builder.Property(audit => audit.CorrelationId).HasMaxLength(100);

        builder.Property(audit => audit.Result)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(audit => audit.OccurredAtUtc).IsRequired();
    }
}
