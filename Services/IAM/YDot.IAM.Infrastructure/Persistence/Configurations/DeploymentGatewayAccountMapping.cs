using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Infrastructure.Configuration;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps PAY's <c>pay_gateway_accounts</c> into this context, read-only.
///
/// <c>ExcludeFromMigrations()</c> IS THE LOAD-BEARING LINE, exactly as it is on PAY's side for
/// IAM's configuration table. Without it IAM's next migration would emit a CREATE TABLE for a
/// table PAY already owns, and the two services would race on start-up - whichever ran second
/// would fail, and which one that is depends on container scheduling.
///
/// NO GLOBAL QUERY FILTER, because the entity does not implement <see cref="Domain.Common.ITenantOwned"/>
/// and deliberately does not: a root user reading every Organisation's gateways is one of the two
/// things this screen is for. What replaces the filter is that the read service applies the scope
/// the QUERY HANDLER resolved, before any other predicate - the same arrangement the configuration
/// table itself uses on this screen.
///
/// <c>settlement_currency_code</c> IS <c>character(3)</c> ON PAY'S SIDE, not varchar. It is
/// declared fixed-length here too, so a value read back is "INR" rather than "INR" with the
/// padding a mismatch would leave attached to it.
/// </summary>
internal sealed class DeploymentGatewayAccountMapping : IEntityTypeConfiguration<DeploymentGatewayAccount>
{
    public void Configure(EntityTypeBuilder<DeploymentGatewayAccount> builder)
    {
        builder.ToTable("pay_gateway_accounts", table => table.ExcludeFromMigrations());

        builder.HasKey(account => account.Id);

        builder.Property(account => account.GatewayName).HasMaxLength(50);
        builder.Property(account => account.MerchantId).HasMaxLength(200);
        builder.Property(account => account.ApiKeyReference).HasMaxLength(200);
        builder.Property(account => account.WebhookSecretReference).HasMaxLength(200);
        builder.Property(account => account.SettlementCurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(account => account.ReturnUrl).HasMaxLength(2000);
        builder.Property(account => account.WebhookUrl).HasMaxLength(2000);
        builder.Property(account => account.EnabledMethods).HasMaxLength(500);
        builder.Property(account => account.Notes).HasMaxLength(2000);
    }
}
