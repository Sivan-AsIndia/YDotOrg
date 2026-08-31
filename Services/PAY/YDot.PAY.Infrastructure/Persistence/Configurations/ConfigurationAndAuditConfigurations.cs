using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Configurations;

/// <summary>
/// A gateway account: which provider an Organisation collects money through.
///
/// NOTHING HERE IS A SECRET. The columns hold REFERENCES to secrets - a key name in a vault or a
/// configuration provider - never the key itself. That is why they are short strings with no
/// encryption converter: encrypting a pointer would be theatre, and storing the real key here
/// would put every Organisation's gateway credentials one SQL injection away from the internet.
/// </summary>
public sealed class PaymentGatewayAccountConfiguration : IEntityTypeConfiguration<PaymentGatewayAccount>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayAccount> builder)
    {
        builder.ToTable("pay_gateway_accounts");

        builder.HasKey(account => account.Id);

        // ONE ACCOUNT PER (Organisation, gateway, mode). Test and live are separate rows on
        // purpose, so a test configuration can never be mistaken for the live one that takes
        // real money.
        builder.HasIndex(account => new { account.TenantId, account.GatewayName, account.IsTestMode })
            .HasDatabaseName("ux_pay_gateway_accounts_tenant_gateway")
            .IsUnique();

        // Webhook resolution: a payload names its merchant, and we need the Organisation.
        builder.HasIndex(account => new { account.GatewayName, account.MerchantId })
            .HasDatabaseName("ux_pay_gateway_accounts_merchant")
            .IsUnique();

        builder.Property(account => account.GatewayName).HasMaxLength(50).IsRequired();
        builder.Property(account => account.MerchantId).HasMaxLength(200).IsRequired();
        builder.Property(account => account.ApiKeyReference).HasMaxLength(200);
        builder.Property(account => account.WebhookSecretReference).HasMaxLength(200);
        builder.Property(account => account.SettlementCurrencyCode)
            .HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(account => account.ReturnUrl).HasMaxLength(2000);
        builder.Property(account => account.WebhookUrl).HasMaxLength(2000);
        builder.Property(account => account.EnabledMethods).HasMaxLength(500);
        builder.Property(account => account.Notes).HasMaxLength(2000);

        builder.Property(account => account.Version).IsConcurrencyToken();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pay_gateway_accounts_link_validity",
            "payment_link_validity_minutes > 0 AND payment_link_validity_minutes <= 43200"));
    }
}

/// <summary>
/// The audit trail for everything that touched money.
///
/// IT IS NOT ORGANISATION-FILTERED, and that is deliberate rather than an oversight. Two of the
/// rows that matter most - a webhook whose signature failed to verify, and a permission denial
/// where no Organisation could be resolved - are written when the ambient Organisation is empty.
/// A global filter would make exactly those rows invisible afterwards, which is the opposite of
/// what an audit trail is for. Reads are scoped explicitly by the query handler instead.
///
/// APPEND-ONLY BY CONVENTION: no update path exists in the module, so there is no concurrency
/// token and no updated-by column to maintain.
/// </summary>
public sealed class PaymentAuditEventConfiguration : IEntityTypeConfiguration<PaymentAuditEvent>
{
    public void Configure(EntityTypeBuilder<PaymentAuditEvent> builder)
    {
        builder.ToTable("pay_audit_events");

        builder.HasKey(auditEvent => auditEvent.Id);

        // The default view: this Organisation's trail, newest first.
        builder.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.OccurredAtUtc })
            .HasDatabaseName("ix_pay_audit_events_tenant_occurred");

        // "What happened to this donation?" - the single most common audit question.
        builder.HasIndex(auditEvent => new { auditEvent.TargetType, auditEvent.TargetId })
            .HasDatabaseName("ix_pay_audit_events_target");

        builder.HasIndex(auditEvent => new { auditEvent.ActionCode, auditEvent.OccurredAtUtc })
            .HasDatabaseName("ix_pay_audit_events_action");

        // Denials, across the whole platform, for the security review.
        builder.HasIndex(auditEvent => new { auditEvent.Result, auditEvent.OccurredAtUtc })
            .HasDatabaseName("ix_pay_audit_events_result");

        builder.HasIndex(auditEvent => auditEvent.CorrelationId)
            .HasDatabaseName("ix_pay_audit_events_correlation")
            .HasFilter("correlation_id IS NOT NULL");

        builder.Property(auditEvent => auditEvent.ActionCode).HasMaxLength(80).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetType).HasMaxLength(80).IsRequired();
        builder.Property(auditEvent => auditEvent.Reason).HasMaxLength(2000);
        builder.Property(auditEvent => auditEvent.IpAddress).HasMaxLength(64);
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(100);

        // JSON, unbounded. Truncating the detail of an audit row destroys the only account of
        // what a request actually contained.
        builder.Property(auditEvent => auditEvent.Metadata).HasColumnType("jsonb");

        builder.Property(auditEvent => auditEvent.Result)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
