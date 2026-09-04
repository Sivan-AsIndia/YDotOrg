using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.PAY.Infrastructure.Gateway;

namespace YDot.PAY.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps IAM's <c>iam_payment_gateway_configurations</c> into this context, read-only.
///
/// <c>ExcludeFromMigrations()</c> IS THE LOAD-BEARING LINE. Without it PAY's next migration would
/// emit a CREATE TABLE for a table IAM already owns, and the two services would race on start-up
/// - whichever ran second would fail, and which one that is depends on container scheduling. IAM
/// owns this table's DDL; PAY only ever reads it.
///
/// NO GLOBAL QUERY FILTER IS ATTACHED, deliberately, and this is the entity where that decision
/// needs stating. The paths that read it are the DONATION and WEBHOOK paths, which run with no
/// session at all: a donor following a payment link and a provider posting a callback both
/// arrive anonymous, and the Organisation is resolved from the intent or the attempt BEFORE this
/// is read. A filter would return nothing on exactly the paths this exists for. What replaces it
/// is that every read takes an explicit TenantId - see
/// <see cref="TenantGatewayConfigurationReader"/>, which has no method that does not.
/// </summary>
internal sealed class TenantGatewayConfigurationMapping
    : IEntityTypeConfiguration<TenantGatewayConfiguration>
{
    public void Configure(EntityTypeBuilder<TenantGatewayConfiguration> builder)
    {
        builder.ToTable(
            "iam_payment_gateway_configurations",
            table => table.ExcludeFromMigrations());

        builder.HasKey(configuration => configuration.Id);

        // The column names are IAM's, produced by the same snake-case convention this context
        // uses, so nothing needs naming by hand. What DOES need declaring is the computed
        // property, which has no column behind it.
        builder.Ignore(configuration => configuration.IsProduction);

        builder.Property(configuration => configuration.Provider).HasMaxLength(40);
        builder.Property(configuration => configuration.Environment).HasMaxLength(20);
        builder.Property(configuration => configuration.DisplayName).HasMaxLength(150);
        builder.Property(configuration => configuration.MerchantId).HasMaxLength(150);
        builder.Property(configuration => configuration.ApiKeyCipher).HasMaxLength(2000);
        builder.Property(configuration => configuration.SecretKeyCipher).HasMaxLength(2000);
        builder.Property(configuration => configuration.WebhookSecretCipher).HasMaxLength(2000);
        builder.Property(configuration => configuration.WebhookUrl).HasMaxLength(500);
        builder.Property(configuration => configuration.ReturnUrl).HasMaxLength(500);
        builder.Property(configuration => configuration.EnabledMethods).HasMaxLength(300);
        builder.Property(configuration => configuration.SettlementCurrencyCode).HasMaxLength(3);
    }
}
