using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Configurations;

/// <summary>
/// The donation flow: intents, attempts, gateway events and donations.
///
/// TWO KINDS OF UNIQUENESS APPEAR HERE AND THE DIFFERENCE IS LOAD-BEARING.
///
/// ORGANISATION-SCOPED, like every other module: nothing, as it happens - every reference in
/// this file is globally unique, and that is the point.
///
/// GLOBALLY UNIQUE: the intent reference, the donation reference, the gateway reference and the
/// gateway event id. All four are resolved by callers who have NO SESSION - a donor following a
/// payment link, a gateway posting a webhook - so there is no Organisation to scope them by at
/// the moment of lookup. Two Organisations sharing one would credit one charity's money to
/// another.
///
/// EVERY MONEY COLUMN IS AN OWNED TYPE, so the amount and its currency sit side by side on the
/// same row. A single-row read is then enough to render a figure correctly, and no query can
/// accidentally return an amount without knowing what it is denominated in.
/// </summary>
public sealed class DonationIntentConfiguration : IEntityTypeConfiguration<DonationIntent>
{
    public void Configure(EntityTypeBuilder<DonationIntent> builder)
    {
        builder.ToTable("pay_donation_intents");

        builder.HasKey(intent => intent.Id);

        // GLOBALLY unique. See the class comment.
        builder.HasIndex(intent => intent.IntentReference)
            .HasDatabaseName("ix_pay_donation_intents_reference")
            .IsUnique();

        // The section 26 lookup: organisation AND normalised e-mail, never e-mail alone.
        builder.HasIndex(intent => new { intent.TenantId, intent.NormalisedEmail })
            .HasDatabaseName("ix_pay_donation_intents_tenant_email");

        builder.HasIndex(intent => new { intent.TenantId, intent.Status, intent.CreatedAtUtc })
            .HasDatabaseName("ix_pay_donation_intents_tenant_status");

        // Attribution reporting groups by campaign constantly.
        builder.HasIndex(intent => intent.CampaignId)
            .HasDatabaseName("ix_pay_donation_intents_campaign");

        // "Which leads converted?" - the section 28 report.
        builder.HasIndex(intent => intent.LeadId)
            .HasDatabaseName("ix_pay_donation_intents_lead")
            .HasFilter("lead_id IS NOT NULL");

        builder.Property(intent => intent.IntentReference).HasMaxLength(64).IsRequired();
        builder.Property(intent => intent.DonorName).HasMaxLength(200).IsRequired();
        builder.Property(intent => intent.Email).HasMaxLength(320).IsRequired();
        builder.Property(intent => intent.NormalisedEmail).HasMaxLength(320).IsRequired();
        builder.Property(intent => intent.Mobile).HasMaxLength(20);
        builder.Property(intent => intent.TaxIdentifier).HasMaxLength(30);
        builder.Property(intent => intent.AddressLine1).HasMaxLength(250);
        builder.Property(intent => intent.AddressLine2).HasMaxLength(250);
        builder.Property(intent => intent.PostalCode).HasMaxLength(20);
        builder.Property(intent => intent.TrackingReference).HasMaxLength(64);
        builder.Property(intent => intent.PaymentLinkUrl).HasMaxLength(2000);
        builder.Property(intent => intent.ConsentVersion).HasMaxLength(50);
        builder.Property(intent => intent.PublicRecognitionName).HasMaxLength(200);
        builder.Property(intent => intent.FailureReason).HasMaxLength(1000);
        builder.Property(intent => intent.CancellationReason).HasMaxLength(1000);

        builder.Property(intent => intent.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(intent => intent.SourceType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(intent => intent.Version).IsConcurrencyToken();

        // The amount and its currency, together on the row.
        builder.OwnsOne(intent => intent.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(intent => intent.Amount).IsRequired();

        builder.HasMany(intent => intent.Attempts)
            .WithOne(attempt => attempt.DonationIntent)
            .HasForeignKey(attempt => attempt.DonationIntentId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT, not cascade. Deleting an intent must never delete the donation recorded
        // against it - the money exists whatever happens to the record of the intention.
        builder.HasOne(intent => intent.Donation)
            .WithOne(donation => donation.DonationIntent)
            .HasForeignKey<Donation>(donation => donation.DonationIntentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pay_donation_intents_amount", "amount > 0");

            // Consent that was given must say when and to what version. A half-recorded consent
            // is worse than none, because it looks like evidence.
            table.HasCheckConstraint(
                "ck_pay_donation_intents_consent",
                "consent_given = false OR consent_given_at_utc IS NOT NULL");

            table.HasCheckConstraint(
                "ck_pay_donation_intents_attempts", "attempt_count >= 0");
        });
    }
}

/// <summary>One attempt at a gateway.</summary>
public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("pay_payment_attempts");

        builder.HasKey(attempt => attempt.Id);

        // GLOBALLY unique and filtered: a webhook arrives carrying only this, with no session to
        // scope it by, and many attempts never reach the gateway at all so their reference is null.
        builder.HasIndex(attempt => attempt.GatewayReference)
            .HasDatabaseName("ix_pay_payment_attempts_gateway_reference")
            .IsUnique()
            .HasFilter("gateway_reference IS NOT NULL");

        // One attempt number per intent. Two "attempt 2" rows make the support timeline a lie.
        builder.HasIndex(attempt => new { attempt.DonationIntentId, attempt.AttemptNumber })
            .HasDatabaseName("ix_pay_payment_attempts_intent_number")
            .IsUnique();

        builder.HasIndex(attempt => new { attempt.TenantId, attempt.Status })
            .HasDatabaseName("ix_pay_payment_attempts_tenant_status");

        builder.Property(attempt => attempt.GatewayName).HasMaxLength(50).IsRequired();
        builder.Property(attempt => attempt.GatewayReference).HasMaxLength(200);
        builder.Property(attempt => attempt.MaskedInstrument).HasMaxLength(50);
        builder.Property(attempt => attempt.GatewayResultCode).HasMaxLength(100);
        builder.Property(attempt => attempt.GatewayMessage).HasMaxLength(2000);
        builder.Property(attempt => attempt.DonorFacingMessage).HasMaxLength(500);
        builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(100);
        builder.Property(attempt => attempt.DonorIpAddress).HasMaxLength(64);
        builder.Property(attempt => attempt.DonorUserAgent).HasMaxLength(400);

        builder.Property(attempt => attempt.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(attempt => attempt.MethodType)
            .HasConversion<string>().HasMaxLength(40);

        builder.Property(attempt => attempt.Version).IsConcurrencyToken();

        builder.OwnsOne(attempt => attempt.RequestedAmount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("requested_amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("requested_currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(attempt => attempt.RequestedAmount).IsRequired();

        builder.OwnsOne(attempt => attempt.CapturedAmount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("captured_amount").HasPrecision(18, 2);

            money.Property(value => value.CurrencyCode)
                .HasColumnName("captured_currency_code").HasMaxLength(3).IsFixedLength();
        });

        builder.HasMany(attempt => attempt.Events)
            .WithOne(paymentEvent => paymentEvent.PaymentAttempt)
            .HasForeignKey(paymentEvent => paymentEvent.PaymentAttemptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pay_payment_attempts_number", "attempt_number > 0"));
    }
}

/// <summary>One thing a gateway told us.</summary>
public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("pay_payment_events");

        builder.HasKey(paymentEvent => paymentEvent.Id);

        // THE DUPLICATE-DELIVERY GUARD, and the most important constraint in this file. Gateways
        // redeliver webhooks for days; without this a redelivered capture would record the
        // donation twice. Unique per GATEWAY, because two providers may issue the same event id.
        builder.HasIndex(paymentEvent => new { paymentEvent.GatewayName, paymentEvent.GatewayEventId })
            .HasDatabaseName("ix_pay_payment_events_gateway_event")
            .IsUnique();

        // The queue's own ordering: outstanding events, oldest first.
        builder.HasIndex(paymentEvent => new { paymentEvent.Status, paymentEvent.ReceivedAtUtc })
            .HasDatabaseName("ix_pay_payment_events_status_received");

        builder.HasIndex(paymentEvent => paymentEvent.GatewayReference)
            .HasDatabaseName("ix_pay_payment_events_gateway_reference")
            .HasFilter("gateway_reference IS NOT NULL");

        builder.Property(paymentEvent => paymentEvent.GatewayName).HasMaxLength(50).IsRequired();
        builder.Property(paymentEvent => paymentEvent.GatewayEventId).HasMaxLength(200).IsRequired();
        builder.Property(paymentEvent => paymentEvent.GatewayReference).HasMaxLength(200);
        builder.Property(paymentEvent => paymentEvent.ProcessingError).HasMaxLength(2000);
        builder.Property(paymentEvent => paymentEvent.DismissalReason).HasMaxLength(1000);

        // UNBOUNDED, deliberately. This is the verbatim provider payload, and truncating it would
        // destroy the one thing that settles an argument with the gateway about what they sent.
        builder.Property(paymentEvent => paymentEvent.RawPayload).HasColumnType("text");

        builder.Property(paymentEvent => paymentEvent.EventType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(paymentEvent => paymentEvent.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(paymentEvent => paymentEvent.Version).IsConcurrencyToken();

        builder.OwnsOne(paymentEvent => paymentEvent.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount").HasPrecision(18, 2);

            money.Property(value => value.CurrencyCode)
                .HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
        });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pay_payment_events_attempts", "processing_attempts >= 0"));
    }
}

/// <summary>Money that actually arrived.</summary>
public sealed class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        builder.ToTable("pay_donations");

        builder.HasKey(donation => donation.Id);

        builder.HasIndex(donation => donation.DonationReference)
            .HasDatabaseName("ix_pay_donations_reference")
            .IsUnique();

        // ONE DONATION PER INTENT. The invariant that stops a double capture becoming double
        // income - two successful captures produce one donation and a refund case, never two
        // donations. Enforced by the database rather than only by the handler's read-then-write.
        builder.HasIndex(donation => donation.DonationIntentId)
            .HasDatabaseName("ix_pay_donations_intent")
            .IsUnique();

        builder.HasIndex(donation => new { donation.TenantId, donation.Status, donation.DonatedAtUtc })
            .HasDatabaseName("ix_pay_donations_tenant_status_date");

        builder.HasIndex(donation => new { donation.TenantId, donation.CampaignId })
            .HasDatabaseName("ix_pay_donations_tenant_campaign");

        builder.HasIndex(donation => donation.DonorId)
            .HasDatabaseName("ix_pay_donations_donor")
            .HasFilter("donor_id IS NOT NULL");

        // The reconciliation queue's index.
        builder.HasIndex(donation => new { donation.TenantId, donation.ReconciliationStatus })
            .HasDatabaseName("ix_pay_donations_tenant_reconciliation");

        builder.Property(donation => donation.DonationReference).HasMaxLength(64).IsRequired();
        builder.Property(donation => donation.DonorName).HasMaxLength(200).IsRequired();
        builder.Property(donation => donation.DonorEmail).HasMaxLength(320).IsRequired();
        builder.Property(donation => donation.DonorMobile).HasMaxLength(20);
        builder.Property(donation => donation.DonorTaxIdentifier).HasMaxLength(30);
        builder.Property(donation => donation.DonorAddress).HasMaxLength(500);
        builder.Property(donation => donation.GatewayReference).HasMaxLength(200);
        builder.Property(donation => donation.SettlementBatchReference).HasMaxLength(100);
        builder.Property(donation => donation.ReconciliationNote).HasMaxLength(1000);

        builder.Property(donation => donation.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(donation => donation.SettlementStatus)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(donation => donation.ReconciliationStatus)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(donation => donation.SourceType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(donation => donation.MethodType)
            .HasConversion<string>().HasMaxLength(40);

        builder.Property(donation => donation.Version).IsConcurrencyToken();

        builder.OwnsOne(donation => donation.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(donation => donation.Amount).IsRequired();

        builder.OwnsOne(donation => donation.RefundedAmount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("refunded_amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("refunded_currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(donation => donation.RefundedAmount).IsRequired();

        builder.OwnsOne(donation => donation.GatewayFee, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("gateway_fee").HasPrecision(18, 2);

            money.Property(value => value.CurrencyCode)
                .HasColumnName("gateway_fee_currency_code").HasMaxLength(3).IsFixedLength();
        });

        builder.OwnsOne(donation => donation.NetAmount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("net_amount").HasPrecision(18, 2);

            money.Property(value => value.CurrencyCode)
                .HasColumnName("net_currency_code").HasMaxLength(3).IsFixedLength();
        });

        builder.HasOne(donation => donation.PaymentAttempt)
            .WithMany()
            .HasForeignKey(donation => donation.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(donation => donation.Receipts)
            .WithOne(receipt => receipt.Donation)
            .HasForeignKey(receipt => receipt.DonationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(donation => donation.RefundCases)
            .WithOne(refundCase => refundCase.Donation)
            .HasForeignKey(refundCase => refundCase.DonationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(donation => donation.ChargebackCases)
            .WithOne(chargeback => chargeback.Donation)
            .HasForeignKey(chargeback => chargeback.DonationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pay_donations_amount", "amount > 0");

            // MORE CANNOT GO BACK THAN CAME IN. The handler checks it too, but this is the
            // backstop that would catch a bug in that check - and the cost of being wrong is
            // paying out money the charity never received.
            table.HasCheckConstraint(
                "ck_pay_donations_refunded", "refunded_amount >= 0 AND refunded_amount <= amount");

            table.HasCheckConstraint(
                "ck_pay_donations_fee", "gateway_fee IS NULL OR gateway_fee >= 0");
        });
    }
}
