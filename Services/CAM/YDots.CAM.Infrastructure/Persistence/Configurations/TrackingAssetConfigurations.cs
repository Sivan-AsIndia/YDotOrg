using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tracking assets, their placements and the custom fields on those placements.
///
/// THE TRACKING REFERENCE IS UNIQUE ACROSS EVERY ORGANISATION, which is the one deliberate
/// exception to the Organisation-scoped uniqueness everywhere else in this module. A reference
/// arrives from the public donation flow with no session and no Organisation to scope it, so it
/// is resolved globally - and a collision between two Organisations would credit one
/// Organisation's gift to another.
/// </summary>
public sealed class TrackingAssetConfiguration : IEntityTypeConfiguration<TrackingAsset>
{
    public void Configure(EntityTypeBuilder<TrackingAsset> builder)
    {
        builder.ToTable("cam_tracking_assets");

        builder.HasKey(asset => asset.Id);

        builder.HasIndex(asset => new { asset.TenantId, asset.Code })
            .HasDatabaseName("ix_cam_tracking_assets_tenant_code")
            .IsUnique();

        // GLOBALLY unique, and filtered so the many Draft assets with no reference yet do not
        // collide with each other on NULL.
        builder.HasIndex(asset => asset.TrackingReference)
            .HasDatabaseName("ix_cam_tracking_assets_reference")
            .IsUnique()
            .HasFilter("tracking_reference IS NOT NULL");

        builder.HasIndex(asset => new { asset.CampaignId, asset.Status })
            .HasDatabaseName("ix_cam_tracking_assets_campaign_status");

        builder.Property(asset => asset.Code).HasMaxLength(50).IsRequired();
        builder.Property(asset => asset.Destination).HasMaxLength(2000).IsRequired();
        builder.Property(asset => asset.ContentTag).HasMaxLength(100);
        builder.Property(asset => asset.GeneratedUrl).HasMaxLength(2000);
        builder.Property(asset => asset.TrackingReference).HasMaxLength(64);

        builder.Property(asset => asset.AssetType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(asset => asset.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(asset => asset.TotalReceived).HasPrecision(18, 2);

        builder.Property(asset => asset.Version).IsConcurrencyToken();

        // RESTRICT on all three reference tables: a channel, source or medium that some asset
        // still uses must not be deletable, because the codes are baked into generated URLs
        // that are already in circulation.
        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(asset => asset.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(asset => asset.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Medium>()
            .WithMany()
            .HasForeignKey(asset => asset.MediumId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(asset => asset.Places)
            .WithOne(place => place.TrackingAsset)
            .HasForeignKey(place => place.TrackingAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_cam_tracking_assets_window", "active_to > active_from");

            table.HasCheckConstraint(
                "ck_cam_tracking_assets_usage", "usage_count >= 0 AND total_received >= 0");

            // An asset past Draft must have been submitted by somebody. Catches a status set
            // directly against the table by an import or a manual fix.
            table.HasCheckConstraint(
                "ck_cam_tracking_assets_submitted",
                "status IN ('Draft') OR submitted_by_user_id IS NOT NULL");
        });
    }
}

/// <summary>One physical or logical placement of a tracking asset.</summary>
public sealed class TrackingAssetPlaceConfiguration : IEntityTypeConfiguration<TrackingAssetPlace>
{
    public void Configure(EntityTypeBuilder<TrackingAssetPlace> builder)
    {
        builder.ToTable("cam_tracking_asset_places");

        builder.HasKey(place => place.Id);

        builder.HasIndex(place => place.TrackingAssetId)
            .HasDatabaseName("ix_cam_tracking_asset_places_asset");

        builder.Property(place => place.PlaceName).HasMaxLength(200).IsRequired();
        builder.Property(place => place.Destination).HasMaxLength(2000).IsRequired();

        builder.Property(place => place.Version).IsConcurrencyToken();

        builder.HasMany(place => place.CustomFields)
            .WithOne(field => field.Place)
            .HasForeignKey(field => field.TrackingAssetPlaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>A name/value pair carried by one placement.</summary>
public sealed class TrackingAssetCustomFieldConfiguration
    : IEntityTypeConfiguration<TrackingAssetCustomField>
{
    public void Configure(EntityTypeBuilder<TrackingAssetCustomField> builder)
    {
        builder.ToTable("cam_tracking_asset_custom_fields");

        builder.HasKey(field => field.Id);

        // One value per field name per placement. Two rows named the same thing make the pair
        // meaningless to whatever reads it.
        builder.HasIndex(field => new { field.TrackingAssetPlaceId, field.FieldName })
            .HasDatabaseName("ix_cam_tracking_asset_custom_fields_unique")
            .IsUnique();

        builder.Property(field => field.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(field => field.Value).HasMaxLength(500).IsRequired();
    }
}
