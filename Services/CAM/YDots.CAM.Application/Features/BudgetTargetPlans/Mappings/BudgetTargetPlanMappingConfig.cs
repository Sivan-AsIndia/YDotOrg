using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.BudgetTargetPlans.Mappings;

/// <summary>Manual mapping for the Budget and Target Plans slice.</summary>
public static class BudgetTargetPlanMappingConfig
{
    /// <summary>
    /// A new plan from an allocate request.
    ///
    /// THE CODE IS NOT SET HERE. It is minted by the handler against the repository, because
    /// minting needs to see what codes already exist and a mapper has no business reaching the
    /// database.
    /// </summary>
    public static BudgetTargetPlan ToEntity(this AllocateBudgetPlanRequest request, Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(campaign);

        return new BudgetTargetPlan
        {
            CampaignId = campaign.Id,
            PlanPeriod = request.PlanPeriod.Trim(),
            TargetDimension = request.TargetDimension.Trim(),
            OwnerUserId = request.OwnerUserId
        };
    }

    /// <summary>
    /// The first version of a newly allocated plan.
    ///
    /// IT STARTS AS A DRAFT. Set here rather than taken from the request, so a plan cannot be
    /// created already approved - which would put figures into a campaign's committed budget
    /// without anybody having agreed to them.
    /// </summary>
    public static BudgetTargetPlanVersion ToFirstVersion(
        this AllocateBudgetPlanRequest request, BudgetTargetPlan plan, Guid currencyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);

        return new BudgetTargetPlanVersion
        {
            BudgetTargetPlanId = plan.Id,
            VersionNumber = 1,
            TargetAmount = request.TargetAmount,
            BudgetAmount = request.BudgetAmount,
            CurrencyId = currencyId,
            BudgetCategory = request.BudgetCategory.Trim(),
            ExpectedVolume = request.ExpectedVolume,
            Assumptions = Clean(request.Assumptions),
            ApprovalState = PlanApprovalState.Draft
        };
    }

    /// <summary>
    /// A revision as a NEW version.
    ///
    /// The version number comes from the plan, which knows what it has already issued. Passing it
    /// in from the caller would let two simultaneous revisions claim the same number.
    /// </summary>
    public static BudgetTargetPlanVersion ToNextVersion(
        this ReviseBudgetPlanRequest request, BudgetTargetPlan plan, Guid currencyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);

        return new BudgetTargetPlanVersion
        {
            BudgetTargetPlanId = plan.Id,
            VersionNumber = plan.NextVersionNumber,
            TargetAmount = request.TargetAmount,
            BudgetAmount = request.BudgetAmount,
            CurrencyId = currencyId,
            BudgetCategory = request.BudgetCategory.Trim(),
            ExpectedVolume = request.ExpectedVolume,
            Assumptions = Clean(request.Assumptions),
            DecisionReason = Clean(request.RevisionReason),
            ApprovalState = PlanApprovalState.Draft
        };
    }

    /// <summary>Applies an edit to a draft version. Refused elsewhere for anything else.</summary>
    public static void ApplyTo(
        this UpdateBudgetPlanVersionRequest request, BudgetTargetPlanVersion version, Guid currencyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(version);

        version.TargetAmount = request.TargetAmount;
        version.BudgetAmount = request.BudgetAmount;
        version.CurrencyId = currencyId;
        version.BudgetCategory = request.BudgetCategory.Trim();
        version.ExpectedVolume = request.ExpectedVolume;
        version.Assumptions = Clean(request.Assumptions);
    }

    /// <summary>
    /// One version as the screen reads it.
    ///
    /// THE ACTUAL AND THE VARIANCE ARE PASSED IN rather than read off the entity, because they are
    /// computed from the donations attributed to the campaign. Storing them on the version would
    /// mean a figure that was right when it was written and drifts from that moment on.
    /// </summary>
    public static BudgetPlanVersionResponse ToResponse(
        this BudgetTargetPlanVersion version,
        string currencyCode,
        decimal actualReconciledAmount)
    {
        ArgumentNullException.ThrowIfNull(version);

        // ONLY THE APPROVED VERSION HAS AN ACTUAL. A draft or superseded version is not what the
        // campaign is being run to, so reporting money against it would invite somebody to compare
        // real income with figures nobody agreed to.
        var actual = version.CountsTowardTotals ? actualReconciledAmount : 0m;
        var variance = version.CountsTowardTotals ? actual - version.TargetAmount : 0m;

        return new BudgetPlanVersionResponse
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            VersionLabel = $"v{version.VersionNumber}",
            TargetAmount = version.TargetAmount,
            BudgetAmount = version.BudgetAmount,
            CurrencyId = version.CurrencyId,
            CurrencyCode = currencyCode,
            BudgetCategory = version.BudgetCategory,
            ExpectedVolume = version.ExpectedVolume,
            Assumptions = version.Assumptions,
            ApprovalState = version.ApprovalState,
            ApprovalStateDescription = Describe(version.ApprovalState),
            SubmittedByUserId = version.SubmittedByUserId,
            SubmittedAtUtc = version.SubmittedAtUtc,
            ApprovedByUserId = version.ApprovedByUserId,
            ApprovedAtUtc = version.ApprovedAtUtc,
            DecisionReason = version.DecisionReason,
            EffectiveAtUtc = version.EffectiveAtUtc,
            ActualReconciledAmount = actual,
            Variance = variance,
            VariancePercentage = Percentage(variance, version.TargetAmount),
            Version = version.Version,
            IsEditable = version.IsEditable,
            CountsTowardTotals = version.CountsTowardTotals
        };
    }

    public static BudgetPlanListItemResponse ToListItemResponse(
        this BudgetTargetPlan plan,
        string campaignCode,
        string campaignName,
        string currencyCode,
        decimal actualReconciledAmount,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // THE APPROVED VERSION WHEN THERE IS ONE, else the latest. A register showing the newest
        // draft next to a colleague's approved figures would present two different answers to "what
        // is this plan?", and the approved one is the answer that governs.
        var display = plan.ApprovedVersion ?? plan.LatestVersion;

        return new BudgetPlanListItemResponse
        {
            Id = plan.Id,
            Code = plan.Code,
            CampaignId = plan.CampaignId,
            CampaignCode = campaignCode,
            CampaignName = campaignName,
            PlanPeriod = plan.PlanPeriod,
            TargetDimension = plan.TargetDimension,
            OwnerUserId = plan.OwnerUserId,
            DisplayVersion = display?.ToResponse(currencyCode, actualReconciledAmount),
            HasApprovedVersion = plan.ApprovedVersion is not null,
            VersionCount = plan.Versions.Count,
            Version = plan.Version,
            PermittedActions = permittedActions
        };
    }

    public static BudgetPlanDetailResponse ToDetailResponse(
        this BudgetTargetPlan plan,
        string campaignCode,
        string campaignName,
        string currencyCode,
        decimal actualReconciledAmount,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var versions = plan.Versions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => version.ToResponse(currencyCode, actualReconciledAmount))
            .ToList();

        return new BudgetPlanDetailResponse
        {
            Id = plan.Id,
            Code = plan.Code,
            TenantId = plan.TenantId,
            BusinessUnitId = plan.BusinessUnitId,
            CampaignId = plan.CampaignId,
            CampaignCode = campaignCode,
            CampaignName = campaignName,
            PlanPeriod = plan.PlanPeriod,
            TargetDimension = plan.TargetDimension,
            OwnerUserId = plan.OwnerUserId,
            Versions = versions,
            ApprovedVersion = versions.FirstOrDefault(version => version.CountsTowardTotals),
            LatestVersion = versions.FirstOrDefault(),
            HasApprovedVersion = plan.ApprovedVersion is not null,
            CreatedAtUtc = plan.CreatedAtUtc,
            CreatedByUserId = plan.CreatedByUserId,
            UpdatedAtUtc = plan.UpdatedAtUtc,
            UpdatedByUserId = plan.UpdatedByUserId,
            Version = plan.Version,
            PermittedActions = permittedActions
        };
    }

    /// <summary>
    /// What a state means, in words a screen can print.
    ///
    /// SUPERSEDED IS SPELLED OUT because it is the one people misread. A superseded version was
    /// approved once and is no longer in force - which is not the same as rejected, and a screen
    /// that made them look alike would suggest the figures had been refused rather than replaced.
    /// </summary>
    public static string Describe(PlanApprovalState state) => state switch
    {
        PlanApprovalState.Draft => "Draft - not yet submitted for approval",
        PlanApprovalState.Submitted => "Submitted - awaiting a decision",
        PlanApprovalState.Approved => "Approved - in force and counted toward the campaign totals",
        PlanApprovalState.Superseded => "Superseded - approved previously, replaced by a later version",
        PlanApprovalState.Rejected => "Rejected - returned for revision",
        _ => state.ToString()
    };

    private static decimal Percentage(decimal variance, decimal target) =>
        target == 0m ? 0m : Math.Round(variance / target * 100m, 2);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
