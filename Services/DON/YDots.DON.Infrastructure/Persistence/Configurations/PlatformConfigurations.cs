using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.Configurations;

/// <summary>Table don_audit_events. Append only.</summary>
public sealed class DonorAuditEventConfiguration : IEntityTypeConfiguration<DonorAuditEvent>
{
    public void Configure(EntityTypeBuilder<DonorAuditEvent> builder)
    {
        builder.ToTable("don_audit_events");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Version).IsConcurrencyToken();

        builder.Property(entry => entry.ActionCode).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.TargetType).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.Result).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(2000);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.IpAddress).HasMaxLength(64);

        // The Activity history panel reads by target, newest first.
        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId, entry.CreatedAtUtc })
            .HasDatabaseName("ix_don_audit_events_target");

        // Support reads by correlation id when somebody quotes a reference from a failure screen.
        builder.HasIndex(entry => entry.CorrelationId)
            .HasDatabaseName("ix_don_audit_events_correlation");

        builder.HasIndex(entry => new { entry.OrganisationId, entry.CreatedAtUtc })
            .HasDatabaseName("ix_don_audit_events_org_created");
    }
}

/// <summary>Table don_outbox_messages.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("don_outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Version).IsConcurrencyToken();

        builder.Property(message => message.EventType).HasMaxLength(120).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.AggregateType).HasMaxLength(120).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(100).IsRequired();

        // A publisher takes unprocessed rows in the order they happened, so that is the index.
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.OccurredAtUtc })
            .HasDatabaseName("ix_don_outbox_unprocessed");

        builder.HasIndex(message => new { message.AggregateType, message.AggregateId })
            .HasDatabaseName("ix_don_outbox_aggregate");
    }
}

/// <summary>Table don_idempotency_keys.</summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("don_idempotency_keys");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Version).IsConcurrencyToken();

        builder.Property(record => record.Key).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Endpoint).HasMaxLength(200).IsRequired();
        builder.Property(record => record.ResourceReference).HasMaxLength(60).IsRequired();

        // The unique index is what actually enforces idempotency: even if two retries arrive at
        // the same moment and both miss the lookup, only one INSERT can survive.
        builder.HasIndex(record => new { record.OrganisationId, record.Key, record.Endpoint })
            .IsUnique()
            .HasDatabaseName("ix_don_idempotency_key_endpoint");
    }
}
