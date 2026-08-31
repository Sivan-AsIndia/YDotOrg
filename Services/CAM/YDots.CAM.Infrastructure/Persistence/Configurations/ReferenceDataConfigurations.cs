using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// The three global reference tables: Channel, Source and Medium.
///
/// THEIR CODES ARE UNIQUE PLATFORM-WIDE, not per Organisation, and that is the whole reason
/// these tables are not Organisation-owned. The codes end up in generated tracking URLs and in
/// attribution reporting that spans Organisations, so one code has to mean one thing everywhere
/// - two Organisations each defining CPC differently would make a cross-Organisation report
/// meaningless.
///
/// THE THREE CONFIGURATIONS ARE IDENTICAL BUT WRITTEN OUT, rather than generated from a shared
/// helper. EF configuration is declarative and read far more often than it is written; a
/// reviewer checking what constrains cam_sources should find it under a type called
/// SourceConfiguration rather than have to work out which loop iteration produced it.
/// </summary>
public sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("cam_channels");

        builder.HasKey(channel => channel.Id);

        builder.HasIndex(channel => channel.Code)
            .HasDatabaseName("ix_cam_channels_code")
            .IsUnique();

        builder.HasIndex(channel => channel.Name)
            .HasDatabaseName("ix_cam_channels_name")
            .IsUnique();

        builder.Property(channel => channel.Code).HasMaxLength(50).IsRequired();
        builder.Property(channel => channel.Name).HasMaxLength(100).IsRequired();
        builder.Property(channel => channel.Description).HasMaxLength(500);

        builder.Property(channel => channel.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(channel => channel.Version).IsConcurrencyToken();
    }
}

/// <summary>Where a visitor came from, in the UTM sense.</summary>
public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("cam_sources");

        builder.HasKey(source => source.Id);

        builder.HasIndex(source => source.Code)
            .HasDatabaseName("ix_cam_sources_code")
            .IsUnique();

        builder.HasIndex(source => source.Name)
            .HasDatabaseName("ix_cam_sources_name")
            .IsUnique();

        builder.Property(source => source.Code).HasMaxLength(50).IsRequired();
        builder.Property(source => source.Name).HasMaxLength(100).IsRequired();
        builder.Property(source => source.Description).HasMaxLength(500);

        builder.Property(source => source.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(source => source.Version).IsConcurrencyToken();
    }
}

/// <summary>How a visitor arrived, in the UTM sense.</summary>
public sealed class MediumConfiguration : IEntityTypeConfiguration<Medium>
{
    public void Configure(EntityTypeBuilder<Medium> builder)
    {
        builder.ToTable("cam_mediums");

        builder.HasKey(medium => medium.Id);

        builder.HasIndex(medium => medium.Code)
            .HasDatabaseName("ix_cam_mediums_code")
            .IsUnique();

        builder.HasIndex(medium => medium.Name)
            .HasDatabaseName("ix_cam_mediums_name")
            .IsUnique();

        builder.Property(medium => medium.Code).HasMaxLength(50).IsRequired();
        builder.Property(medium => medium.Name).HasMaxLength(100).IsRequired();
        builder.Property(medium => medium.Description).HasMaxLength(500);

        builder.Property(medium => medium.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(medium => medium.Version).IsConcurrencyToken();
    }
}
