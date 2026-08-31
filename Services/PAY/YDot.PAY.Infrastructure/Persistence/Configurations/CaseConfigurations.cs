using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Configurations;

/// <summary>
/// Refunds: money going back out.
///
/// THE FILTERED UNIQUE INDEX IS THE POINT OF THIS FILE. One donation may accumulate several
/// refund cases over its life - a rejected request, then a later approved one - but only ONE may
/// be undecided at a time. Without that, two operators each raise a request for the full amount,
/// both get approved by different approvers, and the charity pays out twice what it received.
/// The handler checks it, and Postgres guarantees it.
/// </summary>
public sealed class RefundCaseConfiguration : IEntityTypeConfiguration<RefundCase>
{
    public void Configure(EntityTypeBuilder<RefundCase> builder)
    {
        builder.ToTable("pay_refund_cases");

        builder.HasKey(refundCase => refundCase.Id);

        builder.HasIndex(refundCase => new { refundCase.TenantId, refundCase.CaseReference })
            .HasDatabaseName("ux_pay_refund_cases_reference")
            .IsUnique();

        // AT MOST ONE UNDECIDED REQUEST PER DONATION. See the class comment.
        builder.HasIndex(refundCase => refundCase.DonationId)
            .HasDatabaseName("ux_pay_refund_cases_open_per_donation")
            .IsUnique()
            .HasFilter("status = 'Requested'");

        builder.HasIndex(refundCase => new { refundCase.TenantId, refundCase.Status, refundCase.RequestedAtUtc })
            .HasDatabaseName("ix_pay_refund_cases_tenant_status");

        // The approver's queue is filtered by who asked, because they may not decide their own.
        builder.HasIndex(refundCase => refundCase.RequestedByUserId)
            .HasDatabaseName("ix_pay_refund_cases_requested_by");

        builder.Property(refundCase => refundCase.CaseReference).HasMaxLength(64).IsRequired();
        builder.Property(refundCase => refundCase.ReasonDetail).HasMaxLength(2000);
        builder.Property(refundCase => refundCase.DecisionNote).HasMaxLength(2000);
        builder.Property(refundCase => refundCase.RejectionReason).HasMaxLength(2000);
        builder.Property(refundCase => refundCase.GatewayRefundReference).HasMaxLength(200);
        builder.Property(refundCase => refundCase.GatewayFailureReason).HasMaxLength(2000);

        builder.Property(refundCase => refundCase.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(refundCase => refundCase.Reason)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(refundCase => refundCase.Version).IsConcurrencyToken();

        builder.OwnsOne(refundCase => refundCase.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(refundCase => refundCase.Amount).IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pay_refund_cases_amount", "amount > 0");

            // A decision must record who made it and when. An approval with no approver is not
            // an approval, it is an unexplained payout.
            table.HasCheckConstraint(
                "ck_pay_refund_cases_decision",
                "decided_at_utc IS NULL OR decided_by_user_id IS NOT NULL");

            // A rejection must say why - the requester is entitled to know.
            table.HasCheckConstraint(
                "ck_pay_refund_cases_rejection",
                "status <> 'Rejected' OR rejection_reason IS NOT NULL");
        });
    }
}

/// <summary>
/// Chargebacks: money taken back by the donor's bank.
///
/// UNLIKE A REFUND, THIS IS NOT OUR DECISION. The bank has already moved the money; the case
/// exists so somebody can contest it before the evidence deadline. Hence the deadline column is
/// indexed - the whole queue is sorted by how little time is left.
/// </summary>
public sealed class ChargebackCaseConfiguration : IEntityTypeConfiguration<ChargebackCase>
{
    public void Configure(EntityTypeBuilder<ChargebackCase> builder)
    {
        builder.ToTable("pay_chargeback_cases");

        builder.HasKey(chargeback => chargeback.Id);

        builder.HasIndex(chargeback => new { chargeback.TenantId, chargeback.CaseReference })
            .HasDatabaseName("ux_pay_chargeback_cases_reference")
            .IsUnique();

        // The bank's own dispute id, globally unique where present: an incoming notification
        // carries this and nothing else, so it must not be ambiguous across Organisations.
        builder.HasIndex(chargeback => chargeback.GatewayDisputeReference)
            .HasDatabaseName("ux_pay_chargeback_cases_dispute")
            .IsUnique()
            .HasFilter("gateway_dispute_reference IS NOT NULL");

        builder.HasIndex(chargeback => chargeback.DonationId)
            .HasDatabaseName("ix_pay_chargeback_cases_donation");

        // The urgency queue.
        builder.HasIndex(chargeback => new { chargeback.TenantId, chargeback.Status, chargeback.EvidenceDueAtUtc })
            .HasDatabaseName("ix_pay_chargeback_cases_tenant_due");

        builder.HasIndex(chargeback => chargeback.AssignedToUserId)
            .HasDatabaseName("ix_pay_chargeback_cases_assignee")
            .HasFilter("assigned_to_user_id IS NOT NULL");

        builder.Property(chargeback => chargeback.CaseReference).HasMaxLength(64).IsRequired();
        builder.Property(chargeback => chargeback.GatewayDisputeReference).HasMaxLength(200);
        builder.Property(chargeback => chargeback.ReasonCode).HasMaxLength(50);
        builder.Property(chargeback => chargeback.ReasonDescription).HasMaxLength(1000);
        builder.Property(chargeback => chargeback.EvidenceSummary).HasMaxLength(4000);
        builder.Property(chargeback => chargeback.EvidenceDocumentUrls).HasMaxLength(4000);
        builder.Property(chargeback => chargeback.ResolutionNote).HasMaxLength(2000);

        builder.Property(chargeback => chargeback.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(chargeback => chargeback.Version).IsConcurrencyToken();

        builder.OwnsOne(chargeback => chargeback.DisputedAmount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("disputed_amount").HasPrecision(18, 2).IsRequired();

            money.Property(value => value.CurrencyCode)
                .HasColumnName("disputed_currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(chargeback => chargeback.DisputedAmount).IsRequired();

        builder.OwnsOne(chargeback => chargeback.ChargebackFee, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("chargeback_fee").HasPrecision(18, 2);

            money.Property(value => value.CurrencyCode)
                .HasColumnName("chargeback_fee_currency_code").HasMaxLength(3).IsFixedLength();
        });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pay_chargeback_cases_amount", "disputed_amount > 0");

            table.HasCheckConstraint(
                "ck_pay_chargeback_cases_fee", "chargeback_fee IS NULL OR chargeback_fee >= 0");

            // Submitted evidence must record who submitted it - the bank may ask, and so may we.
            table.HasCheckConstraint(
                "ck_pay_chargeback_cases_evidence",
                "evidence_submitted_at_utc IS NULL OR evidence_submitted_by_user_id IS NOT NULL");
        });
    }
}
