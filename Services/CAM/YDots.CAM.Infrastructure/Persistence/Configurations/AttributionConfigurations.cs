using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>Requests to re-examine which campaign a donation was credited to.</summary>
public sealed class AttributionCorrectionRequestConfiguration
    : IEntityTypeConfiguration<AttributionCorrectionRequest>
{
    public void Configure(EntityTypeBuilder<AttributionCorrectionRequest> builder)
    {
        builder.ToTable("cam_attribution_correction_requests");

        builder.HasKey(request => request.Id);

        // AT MOST ONE OPEN REQUEST PER DONATION. Two people each raising one, each unaware of the
        // other, is exactly what the open flag on the explorer exists to prevent - and a handler
        // check alone cannot stop two simultaneous requests, because both would read "none open".
        builder.HasIndex(request => request.DonationId)
            .HasDatabaseName("ux_cam_attribution_corrections_open")
            .IsUnique()
            .HasFilter("is_resolved = false");

        // "What is outstanding?" - the queue somebody works through.
        builder.HasIndex(request => new { request.TenantId, request.IsResolved })
            .HasDatabaseName("ix_cam_attribution_corrections_queue");

        builder.Property(request => request.DonationReference).HasMaxLength(64).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(request => request.ResolutionNote).HasMaxLength(2000);

        builder.Property(request => request.Version).IsConcurrencyToken();

        // A resolved request records who resolved it and when; an open one records neither. Both
        // halves enforced together, so a half-written resolution cannot exist.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_attribution_corrections_resolution",
            "(is_resolved = false AND resolved_at_utc IS NULL AND resolved_by_user_id IS NULL) "
            + "OR (is_resolved = true AND resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)"));

        // AN UNRESOLVED REQUEST CANNOT CLAIM THE ATTRIBUTION CHANGED. The change is the outcome of
        // a resolution, and a row asserting one without the other would misreport how often
        // tracking actually gets it wrong.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_attribution_corrections_changed",
            "is_resolved = true OR attribution_changed = false"));
    }
}
