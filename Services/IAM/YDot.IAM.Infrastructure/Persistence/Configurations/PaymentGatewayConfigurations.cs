using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities.Configuration;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// The payment gateway configuration table.
///
/// THE UNIQUE INDEX IS THE ONE THING TO READ HERE. (TenantId, Provider, Environment) is the
/// natural key, and it is enforced in the database rather than only in the handler because the
/// handler's check-then-insert has a window: two administrators saving the same new Razorpay
/// sandbox row at once would both find nothing and both insert. The result is two rows for one
/// merchant account, and PAY picking between them by row order.
///
/// NOTE WHAT IS NOT INDEXED. There is no index on the active flag alone. An Organisation holds a
/// handful of these rows - one live, one sandbox, perhaps a retired provider - so every query in
/// the module is already reading a tiny set, and an index would be pure write cost.
/// </summary>
public sealed class PaymentGatewayConfigurationConfiguration
    : IEntityTypeConfiguration<PaymentGatewayConfiguration>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayConfiguration> builder)
    {
        builder.ToTable("iam_payment_gateway_configurations");

        builder.HasKey(configuration => configuration.Id);

        builder.HasIndex(configuration =>
                new { configuration.TenantId, configuration.Provider, configuration.Environment })
            .HasDatabaseName("ix_iam_payment_gateway_configurations_tenant_provider_env")
            .IsUnique();

        // What PAY looks up on: this Organisation's active row for an environment.
        builder.HasIndex(configuration =>
                new { configuration.TenantId, configuration.IsActive, configuration.Environment })
            .HasDatabaseName("ix_iam_payment_gateway_configurations_tenant_active");

        // Enums as text, like every other enum in this service: a member added later cannot
        // renumber the ones already written.
        builder.Property(configuration => configuration.Provider)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(configuration => configuration.Environment)
            .HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(configuration => configuration.DisplayName).HasMaxLength(150);
        builder.Property(configuration => configuration.MerchantId).HasMaxLength(150);

        // THE SEALED COLUMNS. Sized for the base64 envelope rather than for the credential:
        // v1 + nonce + tag + ciphertext runs to roughly a third more than the plaintext, and a
        // column that truncated a ciphertext would store something that silently fails to
        // decrypt.
        builder.Property(configuration => configuration.ApiKeyCipher).HasMaxLength(2000);
        builder.Property(configuration => configuration.SecretKeyCipher).HasMaxLength(2000);
        builder.Property(configuration => configuration.WebhookSecretCipher).HasMaxLength(2000);

        builder.Property(configuration => configuration.ApiKeyHint).HasMaxLength(60);

        builder.Property(configuration => configuration.WebhookUrl).HasMaxLength(500);
        builder.Property(configuration => configuration.ReturnUrl).HasMaxLength(500);
        builder.Property(configuration => configuration.SubscribedEvents).HasMaxLength(500);
        builder.Property(configuration => configuration.EnabledMethods).HasMaxLength(300);

        builder.Property(configuration => configuration.SettlementCurrencyCode)
            .HasMaxLength(3).IsRequired();

        builder.Property(configuration => configuration.LastTestMessage).HasMaxLength(500);
        builder.Property(configuration => configuration.Notes).HasMaxLength(2000);

        builder.Property(configuration => configuration.Version).IsConcurrencyToken();
    }
}

/// <summary>
/// The change log.
///
/// APPEND-ONLY IN PRACTICE - there is no endpoint that updates or deletes a row here - and
/// deliberately NOT related to the configuration by a foreign key. The log outlives the
/// configuration: deleting a row whose credentials once took real money has to leave behind the
/// record of who deleted it, and a cascade would take exactly that record with it.
/// </summary>
public sealed class PaymentGatewayConfigurationAuditConfiguration
    : IEntityTypeConfiguration<PaymentGatewayConfigurationAudit>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayConfigurationAudit> builder)
    {
        builder.ToTable("iam_payment_gateway_config_audits");

        builder.HasKey(entry => entry.Id);

        // The panel's own query: one configuration's history, newest first.
        builder.HasIndex(entry => new { entry.ConfigurationId, entry.OccurredAtUtc })
            .HasDatabaseName("ix_iam_payment_gateway_config_audits_configuration");

        // A root user reading one Organisation's whole gateway history across configurations.
        builder.HasIndex(entry => new { entry.TenantId, entry.OccurredAtUtc })
            .HasDatabaseName("ix_iam_payment_gateway_config_audits_tenant");

        builder.Property(entry => entry.Provider)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(entry => entry.Environment)
            .HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(entry => entry.Action)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(entry => entry.FieldName).HasMaxLength(80);

        // A THOUSAND CHARACTERS EACH, and the writer truncates to fit. A long note in the Notes
        // field must not be able to fail the insert - losing the record of a change because
        // somebody wrote a paragraph would be the wrong trade.
        builder.Property(entry => entry.OldValue).HasMaxLength(1000);
        builder.Property(entry => entry.NewValue).HasMaxLength(1000);

        builder.Property(entry => entry.ActorDisplayName).HasMaxLength(200);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(80);
        builder.Property(entry => entry.IpAddress).HasMaxLength(64);
        builder.Property(entry => entry.Reason).HasMaxLength(1000);

        builder.Property(entry => entry.Version).IsConcurrencyToken();
    }
}
