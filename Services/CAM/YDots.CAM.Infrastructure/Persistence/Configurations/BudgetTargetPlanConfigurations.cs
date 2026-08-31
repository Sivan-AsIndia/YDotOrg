using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Configurations;

/// <summary>The budget and target plans held against a campaign.</summary>
public sealed class BudgetTargetPlanConfiguration : IEntityTypeConfiguration<BudgetTargetPlan>
{
    public void Configure(EntityTypeBuilder<BudgetTargetPlan> builder)
    {
        builder.ToTable("cam_budget_target_plans");

        builder.HasKey(plan => plan.Id);

        // The plan reference people quote. Unique inside an Organisation rather than platform-wide,
        // so two charities may each run a plan called BTP-2026-0001.
        builder.HasIndex(plan => new { plan.TenantId, plan.Code })
            .HasDatabaseName("ux_cam_budget_plans_code")
            .IsUnique();

        // ONE PLAN PER CAMPAIGN, PERIOD AND DIMENSION. Two plans covering the same ground is the
        // duplicate the screen warns about, and warning is not enough on its own: two people
        // allocating simultaneously both see "no duplicate" and both insert. This is what actually
        // stops it, and it is why the screen can report a duplicate as a REFUSAL rather than a
        // suggestion.
        builder.HasIndex(plan => new { plan.CampaignId, plan.PlanPeriod, plan.TargetDimension })
            .HasDatabaseName("ux_cam_budget_plans_dimension")
            .IsUnique();

        // "What is this person accountable for?" - the owner's plan queue.
        builder.HasIndex(plan => new { plan.TenantId, plan.OwnerUserId })
            .HasDatabaseName("ix_cam_budget_plans_owner");

        builder.Property(plan => plan.Code).HasMaxLength(40).IsRequired();
        builder.Property(plan => plan.PlanPeriod).HasMaxLength(100).IsRequired();
        builder.Property(plan => plan.TargetDimension).HasMaxLength(120).IsRequired();

        builder.Property(plan => plan.Version).IsConcurrencyToken();

        builder.HasOne(plan => plan.Campaign)
            .WithMany()
            .HasForeignKey(plan => plan.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(plan => plan.Versions)
            .WithOne(version => version.Plan)
            .HasForeignKey(version => version.BudgetTargetPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Computed on read from the versions; never a column.
        builder.Ignore(plan => plan.ApprovedVersion);
        builder.Ignore(plan => plan.LatestVersion);
        builder.Ignore(plan => plan.NextVersionNumber);
    }
}

/// <summary>One immutable version of a plan.</summary>
public sealed class BudgetTargetPlanVersionConfiguration
    : IEntityTypeConfiguration<BudgetTargetPlanVersion>
{
    public void Configure(EntityTypeBuilder<BudgetTargetPlanVersion> builder)
    {
        builder.ToTable("cam_budget_target_plan_versions");

        builder.HasKey(version => version.Id);

        // Version numbers are unique within a plan and never reused.
        builder.HasIndex(version => new { version.BudgetTargetPlanId, version.VersionNumber })
            .HasDatabaseName("ux_cam_budget_plan_versions_number")
            .IsUnique();

        // AT MOST ONE APPROVED VERSION PER PLAN.
        //
        // This single filtered index is what guarantees a campaign's committed budget is well
        // defined. Without it, two people approving different revisions at the same moment would
        // each read "no approved version yet", each write one, and the campaign's budget would
        // thereafter be the sum of two plans that were meant to replace one another.
        builder.HasIndex(version => version.BudgetTargetPlanId)
            .HasDatabaseName("ux_cam_budget_plan_versions_approved")
            .IsUnique()
            .HasFilter("approval_state = 'Approved'");

        builder.Property(version => version.TargetAmount).HasPrecision(18, 2);
        builder.Property(version => version.BudgetAmount).HasPrecision(18, 2);

        builder.Property(version => version.BudgetCategory).HasMaxLength(120).IsRequired();
        builder.Property(version => version.Assumptions).HasMaxLength(4000);
        builder.Property(version => version.DecisionReason).HasMaxLength(2000);

        builder.Property(version => version.ApprovalState)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(version => version.Version).IsConcurrencyToken();

        // A submitted version records who submitted it and when; an unsubmitted one records
        // neither. Enforced as a pair so a half-written submission cannot exist - which matters
        // because the segregation-of-duties check reads exactly these columns.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_budget_plan_versions_submission",
            "(submitted_by_user_id IS NULL AND submitted_at_utc IS NULL) "
            + "OR (submitted_by_user_id IS NOT NULL AND submitted_at_utc IS NOT NULL)"));

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_budget_plan_versions_approval",
            "(approved_by_user_id IS NULL AND approved_at_utc IS NULL) "
            + "OR (approved_by_user_id IS NOT NULL AND approved_at_utc IS NOT NULL)"));

        // A NEGATIVE BUDGET OR TARGET IS NOT A PLAN. Allowed to be zero - a plan may legitimately
        // target nothing while a campaign is being set up - but never below it.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cam_budget_plan_versions_amounts",
            "target_amount >= 0 AND budget_amount >= 0 AND expected_volume >= 0"));

        builder.Ignore(version => version.IsEditable);
        builder.Ignore(version => version.CountsTowardTotals);
    }
}
