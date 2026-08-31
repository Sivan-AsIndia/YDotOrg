using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Configurations;

/// <summary>
/// Receipts, their delivery attempts and the counter that numbers them.
///
/// THE RECEIPT NUMBER IS THE HARDEST CONSTRAINT IN THE MODULE. A tax receipt number has to be
/// unique, gap-free and sequential within an Organisation and a financial year, because the tax
/// authority reads a gap as a destroyed receipt. That produces three rules here that look fussy
/// and are not:
///
///   1. The number is UNIQUE per (Organisation, financial year) but NULL while the receipt is a
///      draft - so the index is filtered. A draft has no number precisely because a number, once
///      taken, can never be given back.
///   2. Voiding a receipt does NOT free its number. The row stays, marked void, and a correction
///      takes the NEXT number and points back at what it supersedes.
///   3. The counter is a separate one-row-per-scope table so that a row lock serialises two
///      simultaneous issues against each other and nothing else.
/// </summary>
public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("pay_receipts");

        builder.HasKey(receipt => receipt.Id);

        // THE GAP-FREE GUARANTEE, enforced by the database rather than trusted to the allocator.
        // Filtered because drafts have no number yet and Postgres would otherwise treat every
        // NULL as distinct anyway - the filter states the intent instead of relying on that.
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.FinancialYear, receipt.ReceiptNumber })
            .HasDatabaseName("ux_pay_receipts_number")
            .IsUnique()
            .HasFilter("receipt_number IS NOT NULL");

        // A donation may have several receipts over time (an original plus corrections), so this
        // one is NOT unique - the "current" one is found by status.
        builder.HasIndex(receipt => receipt.DonationId)
            .HasDatabaseName("ix_pay_receipts_donation");

        builder.HasIndex(receipt => new { receipt.TenantId, receipt.Status, receipt.IssuedAtUtc })
            .HasDatabaseName("ix_pay_receipts_tenant_status");

        // The "receipts that never reached the donor" worklist.
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.DeliveryStatus })
            .HasDatabaseName("ix_pay_receipts_tenant_delivery");

        builder.Property(receipt => receipt.ReceiptNumber).HasMaxLength(64);
        builder.Property(receipt => receipt.FinancialYear).HasMaxLength(9).IsRequired();
        builder.Property(receipt => receipt.DonorName).HasMaxLength(200).IsRequired();
        builder.Property(receipt => receipt.DonorEmail).HasMaxLength(320).IsRequired();
        builder.Property(receipt => receipt.DonorAddress).HasMaxLength(500);
        builder.Property(receipt => receipt.DonorTaxIdentifier).HasMaxLength(30);
        builder.Property(receipt => receipt.CampaignOrFundName).HasMaxLength(200);
        builder.Property(receipt => receipt.OrganisationTaxReference).HasMaxLength(50);
        builder.Property(receipt => receipt.TaxExemptionReference).HasMaxLength(100);
        builder.Property(receipt => receipt.VoidReason).HasMaxLength(1000);
        builder.Property(receipt => receipt.CorrectionReason).HasMaxLength(1000);
        builder.Property(receipt => receipt.DocumentUrl).HasMaxLength(2000);

        builder.Property(receipt => receipt.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(receipt => receipt.DeliveryStatus)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(receipt => receipt.Version).IsConcurrencyToken();

        builder.OwnsOne(receipt => receipt.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(receipt => receipt.Amount).IsRequired();

        // A correction points at what it replaces. RESTRICT, because deleting the superseded
        // receipt would leave the correction claiming to correct nothing.
        builder.HasOne(receipt => receipt.Supersedes)
            .WithMany()
            .HasForeignKey(receipt => receipt.SupersedesReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(receipt => receipt.Deliveries)
            .WithOne(delivery => delivery.Receipt)
            .HasForeignKey(delivery => delivery.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pay_receipts_amount", "amount > 0");

            table.HasCheckConstraint("ck_pay_receipts_version_number", "version_number > 0");

            // An issued receipt must have a number and a date. Issuing is the moment the document
            // becomes a legal artefact, and a legal artefact with no number is unusable.
            table.HasCheckConstraint(
                "ck_pay_receipts_issued",
                "status <> 'Issued' OR (receipt_number IS NOT NULL AND issued_at_utc IS NOT NULL)");

            // A void must say why. "Void, reason unknown" is exactly the record an auditor
            // challenges.
            table.HasCheckConstraint(
                "ck_pay_receipts_voided",
                "voided_at_utc IS NULL OR void_reason IS NOT NULL");
        });
    }
}

/// <summary>Every attempt to get a receipt to its donor, kept whether or not it worked.</summary>
public sealed class ReceiptDeliveryConfiguration : IEntityTypeConfiguration<ReceiptDelivery>
{
    public void Configure(EntityTypeBuilder<ReceiptDelivery> builder)
    {
        builder.ToTable("pay_receipt_deliveries");

        builder.HasKey(delivery => delivery.Id);

        builder.HasIndex(delivery => new { delivery.ReceiptId, delivery.AttemptedAtUtc })
            .HasDatabaseName("ix_pay_receipt_deliveries_receipt");

        builder.HasIndex(delivery => new { delivery.TenantId, delivery.Status })
            .HasDatabaseName("ix_pay_receipt_deliveries_tenant_status");

        builder.Property(delivery => delivery.Channel).HasMaxLength(30).IsRequired();
        builder.Property(delivery => delivery.Destination).HasMaxLength(320).IsRequired();
        builder.Property(delivery => delivery.FailureReason).HasMaxLength(1000);
        builder.Property(delivery => delivery.ProviderReference).HasMaxLength(200);

        builder.Property(delivery => delivery.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(delivery => delivery.Version).IsConcurrencyToken();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pay_receipt_deliveries_failure",
            "status <> 'Failed' OR failure_reason IS NOT NULL"));
    }
}

/// <summary>
/// The counter behind the receipt number.
///
/// ONE ROW PER (Organisation, financial year), and the unique index is what makes the row
/// findable for locking. Note it is NOT an <c>ITenantOwned</c> entity and therefore not query
/// filtered: the allocator looks it up by an explicit Organisation while holding a transaction,
/// and a global filter reading an ambient context would silently return nothing on the webhook
/// path where no Organisation is resolved - which would issue every receipt the number 1.
/// </summary>
public sealed class ReceiptNumberCounterConfiguration : IEntityTypeConfiguration<ReceiptNumberCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptNumberCounter> builder)
    {
        builder.ToTable("pay_receipt_number_counters");

        builder.HasKey(counter => counter.Id);

        builder.HasIndex(counter => new { counter.TenantId, counter.FinancialYear })
            .HasDatabaseName("ux_pay_receipt_number_counters_scope")
            .IsUnique();

        builder.Property(counter => counter.FinancialYear).HasMaxLength(9).IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pay_receipt_number_counters_last", "last_number >= 0"));
    }
}
