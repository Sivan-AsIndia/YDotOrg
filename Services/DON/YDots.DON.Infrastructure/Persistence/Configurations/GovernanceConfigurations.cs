using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.Configurations;

/// <summary>Table don_consents. Append only: nothing here is ever updated in place.</summary>
public sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("don_consents");
        builder.HasKey(consent => consent.Id);
        builder.Property(consent => consent.Version).IsConcurrencyToken();

        builder.Property(consent => consent.Name).HasMaxLength(160).IsRequired();
        builder.Property(consent => consent.Description).HasMaxLength(2000);
        builder.Property(consent => consent.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(consent => consent.Purpose).HasMaxLength(2000).IsRequired();
        builder.Property(consent => consent.Channel).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(consent => consent.ConsentState).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(consent => consent.NoticeVersion).HasMaxLength(60).IsRequired();
        builder.Property(consent => consent.EvidenceSource).HasMaxLength(200).IsRequired();
        builder.Property(consent => consent.EvidenceReference).HasMaxLength(300);
        builder.Property(consent => consent.ContactRestrictions).HasMaxLength(300);
        builder.Property(consent => consent.CorrectionReason).HasMaxLength(2000);
        builder.Property(consent => consent.WithdrawalReason).HasMaxLength(2000);
        builder.Property(consent => consent.CapturedByName).HasMaxLength(200);

        builder.HasIndex(consent => new { consent.DonorId, consent.Channel, consent.Status })
            .HasDatabaseName("ix_don_consents_donor_channel_status");

        builder.HasIndex(consent => consent.LeadId).HasDatabaseName("ix_don_consents_lead");

        builder.HasIndex(consent => consent.EffectiveAtUtc).HasDatabaseName("ix_don_consents_effective");
    }
}

/// <summary>Table don_donor_merge_cases.</summary>
public sealed class DonorMergeCaseConfiguration : IEntityTypeConfiguration<DonorMergeCase>
{
    public void Configure(EntityTypeBuilder<DonorMergeCase> builder)
    {
        builder.ToTable("don_donor_merge_cases");
        builder.HasKey(mergeCase => mergeCase.Id);
        builder.Property(mergeCase => mergeCase.Version).IsConcurrencyToken();

        builder.Property(mergeCase => mergeCase.Name).HasMaxLength(160).IsRequired();
        builder.Property(mergeCase => mergeCase.Description).HasMaxLength(2000);
        builder.Property(mergeCase => mergeCase.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(mergeCase => mergeCase.ReviewReference).HasMaxLength(40).IsRequired();
        builder.Property(mergeCase => mergeCase.ContactComparison).HasMaxLength(1000);
        builder.Property(mergeCase => mergeCase.IdentityConfidence).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(mergeCase => mergeCase.MatchingEvidence).HasMaxLength(2000);
        builder.Property(mergeCase => mergeCase.ConflictingFields).HasMaxLength(1000);
        builder.Property(mergeCase => mergeCase.DonationHistoryImpact).HasMaxLength(1000);
        builder.Property(mergeCase => mergeCase.ConsentImpact).HasMaxLength(1000);
        builder.Property(mergeCase => mergeCase.Decision).HasConversion<string>().HasMaxLength(80);
        builder.Property(mergeCase => mergeCase.DecisionReason).HasMaxLength(2000);
        builder.Property(mergeCase => mergeCase.MergePreview).HasMaxLength(2000);
        builder.Property(mergeCase => mergeCase.DecidedByName).HasMaxLength(200);

        builder.HasIndex(mergeCase => mergeCase.ReviewReference)
            .IsUnique()
            .HasDatabaseName("ix_don_merge_cases_reference");

        builder.HasIndex(mergeCase => new { mergeCase.CandidateADonorId, mergeCase.CandidateBDonorId })
            .HasDatabaseName("ix_don_merge_cases_pair");

        builder.HasIndex(mergeCase => new { mergeCase.Status, mergeCase.UpdatedAtUtc })
            .HasDatabaseName("ix_don_merge_cases_status_updated");

        // RESTRICT on both sides. A merge case is the evidence behind a decision about two
        // people's records; deleting a donor must never silently take it with them.
        builder.HasOne(mergeCase => mergeCase.CandidateADonor)
            .WithMany()
            .HasForeignKey(mergeCase => mergeCase.CandidateADonorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(mergeCase => mergeCase.CandidateBDonor)
            .WithMany()
            .HasForeignKey(mergeCase => mergeCase.CandidateBDonorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Table don_donor_identity_verifications.</summary>
public sealed class DonorIdentityVerificationConfiguration : IEntityTypeConfiguration<DonorIdentityVerification>
{
    public void Configure(EntityTypeBuilder<DonorIdentityVerification> builder)
    {
        builder.ToTable("don_donor_identity_verifications");
        builder.HasKey(verification => verification.Id);
        builder.Property(verification => verification.Version).IsConcurrencyToken();

        builder.Property(verification => verification.VerificationReference).HasMaxLength(40).IsRequired();
        builder.Property(verification => verification.VerificationPurpose).HasMaxLength(2000);
        builder.Property(verification => verification.VerificationChannel).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(verification => verification.MaskedDestination).HasMaxLength(200);
        builder.Property(verification => verification.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(verification => verification.IdentityConfidence).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(verification => verification.EvidenceReference).HasMaxLength(300);
        builder.Property(verification => verification.ReviewerName).HasMaxLength(200);
        builder.Property(verification => verification.ChallengeCodeHash).HasMaxLength(200);
        builder.Property(verification => verification.EscalationReason).HasMaxLength(2000);
        builder.Property(verification => verification.CancellationReason).HasMaxLength(2000);

        builder.HasIndex(verification => verification.VerificationReference)
            .IsUnique()
            .HasDatabaseName("ix_don_verifications_reference");

        builder.HasIndex(verification => new { verification.DonorId, verification.Status })
            .HasDatabaseName("ix_don_verifications_donor_status");

        builder.HasOne(verification => verification.Donor)
            .WithMany()
            .HasForeignKey(verification => verification.DonorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Table don_follow_up_tasks.</summary>
public sealed class FollowUpTaskConfiguration : IEntityTypeConfiguration<FollowUpTask>
{
    public void Configure(EntityTypeBuilder<FollowUpTask> builder)
    {
        builder.ToTable("don_follow_up_tasks");
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Version).IsConcurrencyToken();

        builder.Property(task => task.FollowUpReference).HasMaxLength(40).IsRequired();
        builder.Property(task => task.RelationshipOwnerName).HasMaxLength(200);
        builder.Property(task => task.Purpose).HasMaxLength(2000);
        builder.Property(task => task.PermittedChannel).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(task => task.PreferredLanguage).HasMaxLength(20).IsRequired();
        builder.Property(task => task.NextAction).HasMaxLength(300);
        builder.Property(task => task.Priority).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(task => task.Notes).HasMaxLength(2000);
        builder.Property(task => task.ConsentNoticeVersion).HasMaxLength(60);
        builder.Property(task => task.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(task => task.CompletionOutcome).HasMaxLength(2000);
        builder.Property(task => task.RescheduleReason).HasMaxLength(2000);
        builder.Property(task => task.CancellationReason).HasMaxLength(2000);

        builder.HasIndex(task => task.FollowUpReference)
            .IsUnique()
            .HasDatabaseName("ix_don_follow_ups_reference");

        builder.HasIndex(task => new { task.Status, task.DueAtUtc })
            .HasDatabaseName("ix_don_follow_ups_status_due");

        builder.HasIndex(task => task.RelationshipOwnerUserId)
            .HasDatabaseName("ix_don_follow_ups_owner");

        builder.HasOne(task => task.Donor)
            .WithMany()
            .HasForeignKey(task => task.DonorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(task => task.Lead)
            .WithMany()
            .HasForeignKey(task => task.LeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
