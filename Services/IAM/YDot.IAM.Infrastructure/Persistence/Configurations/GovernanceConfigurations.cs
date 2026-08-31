using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Access requests, access reviews, login-identifier changes, bulk jobs, and the platform
/// tables (audit, outbox, idempotency).
/// </summary>
public sealed class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    public void Configure(EntityTypeBuilder<AccessRequest> builder)
    {
        builder.ToTable("iam_access_requests");

        builder.HasKey(request => request.Id);

        builder.HasIndex(request => new { request.TenantId, request.RequestNumber })
            .HasDatabaseName("ix_iam_access_requests_number")
            .IsUnique();

        builder.HasIndex(request => new { request.TenantId, request.Status })
            .HasDatabaseName("ix_iam_access_requests_tenant_status");

        builder.HasIndex(request => request.RequestedForUserId)
            .HasDatabaseName("ix_iam_access_requests_subject");

        builder.Property(request => request.RequestNumber).HasMaxLength(40).IsRequired();
        builder.Property(request => request.PermissionCode).HasMaxLength(100);
        builder.Property(request => request.ScopeValue).HasMaxLength(200);
        builder.Property(request => request.BusinessJustification).HasMaxLength(1000).IsRequired();
        builder.Property(request => request.DecisionNotes).HasMaxLength(1000);
        builder.Property(request => request.WithdrawalReason).HasMaxLength(500);

        builder.Property(request => request.RequestType).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(request => request.ScopeType).HasConversion<string>().HasMaxLength(40);
        builder.Property(request => request.Version).IsConcurrencyToken();

        builder.HasOne(request => request.RequestedForUser)
            .WithMany()
            .HasForeignKey(request => request.RequestedForUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(request => request.Role)
            .WithMany()
            .HasForeignKey(request => request.RoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(table =>
        {
            // The justification is the whole point of the record, so a token one is refused.
            table.HasCheckConstraint(
                "ck_iam_access_requests_justification_length",
                "length(business_justification) >= 10");

            // MAKER AND CHECKER, enforced by the database. A handler bug cannot produce a row
            // where somebody approved their own request.
            table.HasCheckConstraint(
                "ck_iam_access_requests_independent_approver",
                "decided_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id");

            table.HasCheckConstraint(
                "ck_iam_access_requests_window",
                "access_ends_at_utc IS NULL OR access_ends_at_utc > access_starts_at_utc");
        });
    }
}

/// <summary>A batch of reviews issued together.</summary>
public sealed class AccessReviewCampaignConfiguration : IEntityTypeConfiguration<AccessReviewCampaign>
{
    public void Configure(EntityTypeBuilder<AccessReviewCampaign> builder)
    {
        builder.ToTable("iam_access_review_campaigns");

        builder.HasKey(campaign => campaign.Id);

        builder.HasIndex(campaign => new { campaign.TenantId, campaign.Code })
            .HasDatabaseName("ix_iam_access_review_campaigns_code")
            .IsUnique();

        builder.Property(campaign => campaign.Code).HasMaxLength(50).IsRequired();
        builder.Property(campaign => campaign.Name).HasMaxLength(200).IsRequired();
        builder.Property(campaign => campaign.Description).HasMaxLength(1000);

        builder.Property(campaign => campaign.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(campaign => campaign.Version).IsConcurrencyToken();
    }
}

/// <summary>One reviewer being asked whether one person should still hold what they hold.</summary>
public sealed class AccessReviewConfiguration : IEntityTypeConfiguration<AccessReview>
{
    public void Configure(EntityTypeBuilder<AccessReview> builder)
    {
        builder.ToTable("iam_access_reviews");

        builder.HasKey(review => review.Id);

        builder.HasIndex(review => new { review.TenantId, review.ReviewNumber })
            .HasDatabaseName("ix_iam_access_reviews_number")
            .IsUnique();

        // The reviewer queue is "mine, still open, soonest first".
        builder.HasIndex(review => new { review.ReviewerUserId, review.Status, review.ReviewDueAtUtc })
            .HasDatabaseName("ix_iam_access_reviews_reviewer_queue");

        builder.HasIndex(review => review.CampaignId)
            .HasDatabaseName("ix_iam_access_reviews_campaign");

        builder.Property(review => review.ReviewNumber).HasMaxLength(40).IsRequired();
        builder.Property(review => review.DecisionReason).HasMaxLength(1000);
        builder.Property(review => review.CancellationReason).HasMaxLength(500);

        // The snapshot of what was held when the review was raised, so a later change cannot
        // alter what the reviewer was actually asked about.
        builder.Property(review => review.AccessSnapshot).HasMaxLength(4000);

        builder.Property(review => review.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(review => review.Decision).HasConversion<string>().HasMaxLength(40);
        builder.Property(review => review.Version).IsConcurrencyToken();

        builder.HasOne(review => review.Campaign)
            .WithMany(campaign => campaign.Reviews)
            .HasForeignKey(review => review.CampaignId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(review => review.SubjectUser)
            .WithMany()
            .HasForeignKey(review => review.SubjectUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(review => review.Role)
            .WithMany()
            .HasForeignKey(review => review.RoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(table =>
            // Modify and Revoke both take something away, so the person losing it gets an
            // explanation. Enforced here so no code path can skip it.
            table.HasCheckConstraint(
                "ck_iam_access_reviews_decision_has_reason",
                "decision IS NULL OR decision = 'Retain' OR decision_reason IS NOT NULL"));
    }
}

/// <summary>IAM-USR-05: changing the identifier somebody signs in with.</summary>
public sealed class LoginIdentifierChangeConfiguration : IEntityTypeConfiguration<LoginIdentifierChangeRequest>
{
    public void Configure(EntityTypeBuilder<LoginIdentifierChangeRequest> builder)
    {
        builder.ToTable("iam_login_identifier_changes");

        builder.HasKey(request => request.Id);

        // One open request per user, so two changes cannot race each other.
        builder.HasIndex(request => request.UserId)
            .HasDatabaseName("ix_iam_login_identifier_changes_open")
            .IsUnique()
            .HasFilter("status IN ('Draft', 'PendingVerification', 'PendingApproval', 'Approved')");

        builder.Property(request => request.CurrentValue).HasMaxLength(320).IsRequired();
        builder.Property(request => request.RequestedValue).HasMaxLength(320).IsRequired();
        builder.Property(request => request.NormalizedRequestedValue).HasMaxLength(320).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(1000);
        builder.Property(request => request.RejectionReason).HasMaxLength(1000);

        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(request => request.Version).IsConcurrencyToken();

        builder.HasOne(request => request.User)
            .WithMany()
            .HasForeignKey(request => request.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>IAM-USR-06: bulk jobs.</summary>
public sealed class BulkOperationConfiguration : IEntityTypeConfiguration<BulkOperation>
{
    public void Configure(EntityTypeBuilder<BulkOperation> builder)
    {
        builder.ToTable("iam_bulk_operations");

        builder.HasKey(operation => operation.Id);

        builder.HasIndex(operation => new { operation.TenantId, operation.OperationNumber })
            .HasDatabaseName("ix_iam_bulk_operations_number")
            .IsUnique();

        builder.HasIndex(operation => new { operation.TenantId, operation.Status })
            .HasDatabaseName("ix_iam_bulk_operations_tenant_status");

        builder.Property(operation => operation.OperationNumber).HasMaxLength(40).IsRequired();
        builder.Property(operation => operation.SourceFileName).HasMaxLength(260);
        builder.Property(operation => operation.SourceStoragePath).HasMaxLength(1000);
        builder.Property(operation => operation.ActionParameters).HasMaxLength(4000);
        builder.Property(operation => operation.CancellationReason).HasMaxLength(500);
        builder.Property(operation => operation.FailureSummary).HasMaxLength(2000);
        builder.Property(operation => operation.ResultStoragePath).HasMaxLength(1000);
        builder.Property(operation => operation.CorrelationId).HasMaxLength(80);

        builder.Property(operation => operation.ActionType).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(operation => operation.Version).IsConcurrencyToken();
    }
}

/// <summary>One row of a bulk job, with its own outcome.</summary>
public sealed class BulkOperationItemConfiguration : IEntityTypeConfiguration<BulkOperationItem>
{
    public void Configure(EntityTypeBuilder<BulkOperationItem> builder)
    {
        builder.ToTable("iam_bulk_operation_items");

        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.BulkOperationId, item.RowNumber })
            .HasDatabaseName("ix_iam_bulk_operation_items_row")
            .IsUnique();

        builder.Property(item => item.SourceIdentifier).HasMaxLength(320);
        builder.Property(item => item.SourceData).HasMaxLength(4000);
        builder.Property(item => item.ValidationMessage).HasMaxLength(1000);
        builder.Property(item => item.ResultMessage).HasMaxLength(1000);
        builder.Property(item => item.Version).IsConcurrencyToken();

        builder.HasOne(item => item.BulkOperation)
            .WithMany(operation => operation.Items)
            .HasForeignKey(item => item.BulkOperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// The append-only audit trail.
///
/// NO CASCADE ANYWHERE NEAR THIS TABLE. Deleting a user must never delete the record of what
/// they did — that is the one thing an audit trail exists to prevent.
/// </summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("iam_audit_events");

        builder.HasKey(auditEvent => auditEvent.Id);

        builder.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.OccurredAtUtc })
            .HasDatabaseName("ix_iam_audit_events_tenant_time")
            .IsDescending(false, true);

        builder.HasIndex(auditEvent => new { auditEvent.TargetType, auditEvent.TargetId })
            .HasDatabaseName("ix_iam_audit_events_target");

        builder.HasIndex(auditEvent => auditEvent.ActorUserId)
            .HasDatabaseName("ix_iam_audit_events_actor");

        builder.HasIndex(auditEvent => auditEvent.CorrelationId)
            .HasDatabaseName("ix_iam_audit_events_correlation");

        builder.HasIndex(auditEvent => auditEvent.ActionCode)
            .HasDatabaseName("ix_iam_audit_events_action");

        builder.Property(auditEvent => auditEvent.ActorDisplayName).HasMaxLength(160);
        builder.Property(auditEvent => auditEvent.ActionCode).HasMaxLength(100).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetDisplayName).HasMaxLength(300);
        builder.Property(auditEvent => auditEvent.Reason).HasMaxLength(1000);
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(80).IsRequired();
        builder.Property(auditEvent => auditEvent.IpAddress).HasMaxLength(64);
        builder.Property(auditEvent => auditEvent.UserAgent).HasMaxLength(400);
        builder.Property(auditEvent => auditEvent.RequestPath).HasMaxLength(300);

        // Redacted JSON. Generous, because a change set across many fields is legitimately
        // large - but it never contains a credential, because the writer scrubs first.
        builder.Property(auditEvent => auditEvent.Metadata).HasMaxLength(8000);

        builder.Property(auditEvent => auditEvent.Result).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(auditEvent => auditEvent.ActorScope).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(auditEvent => auditEvent.ClientType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(auditEvent => auditEvent.Version).IsConcurrencyToken();
    }
}

/// <summary>Integration events awaiting publication.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("iam_outbox_messages");

        builder.HasKey(message => message.Id);

        // The publisher drains "unprocessed, oldest first", so the index is filtered to
        // exactly that set and stays small however large the table grows.
        builder.HasIndex(message => message.OccurredAtUtc)
            .HasDatabaseName("ix_iam_outbox_messages_pending")
            .HasFilter("processed_at_utc IS NULL AND is_dead_lettered = FALSE");

        builder.Property(message => message.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).HasMaxLength(8000).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);
        builder.Property(message => message.CorrelationId).HasMaxLength(80);
        builder.Property(message => message.Version).IsConcurrencyToken();
    }
}

/// <summary>Remembered answers to requests that carried an Idempotency-Key.</summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("iam_idempotency_records");

        builder.HasKey(record => record.Id);

        // Scoped by Organisation as well as endpoint, so two Organisations cannot collide on
        // a client-chosen string.
        builder.HasIndex(record => new { record.TenantId, record.Endpoint, record.IdempotencyKey })
            .HasDatabaseName("ix_iam_idempotency_records_key")
            .IsUnique();

        builder.Property(record => record.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Endpoint).HasMaxLength(300).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(record => record.ResponseBody).HasMaxLength(8000);
        builder.Property(record => record.Version).IsConcurrencyToken();
    }
}
