using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.CampaignReadiness.Mappings;

/// <summary>Manual mapping for the Campaign Readiness slice.</summary>
public static class ReadinessMappingConfig
{
    public static CampaignReadinessCheck ToEntity(this CreateReadinessCheckRequest request, Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(campaign);

        return new CampaignReadinessCheck
        {
            CampaignId = campaign.Id,
            CheckName = request.CheckName.Trim(),
            Description = Clean(request.Description),
            Category = request.Category,
            SuccessCriteria = request.SuccessCriteria.Trim(),
            RequiredForLaunch = request.RequiredForLaunch,
            OwnerUserId = request.OwnerUserId,
            DueDate = request.DueDate,
            Notes = Clean(request.Notes),

            // A new check has not been looked at yet. Set here rather than taken from the
            // request, so a check cannot be created already signed off.
            Status = ReadinessCheckStatus.Pending
        };
    }

    public static void ApplyTo(this UpdateReadinessCheckRequest request, CampaignReadinessCheck check)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(check);

        check.CheckName = request.CheckName.Trim();
        check.Description = Clean(request.Description);
        check.Category = request.Category;
        check.SuccessCriteria = request.SuccessCriteria.Trim();
        check.RequiredForLaunch = request.RequiredForLaunch;
        check.OwnerUserId = request.OwnerUserId;
        check.DueDate = request.DueDate;
        check.Notes = Clean(request.Notes);
    }

    public static ReadinessCheckListItemResponse ToListItemResponse(
        this CampaignReadinessCheck check, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(check);

        return new ReadinessCheckListItemResponse(
            check.Id,
            check.CheckName,
            check.Description,
            check.Category,
            DescribeCategory(check.Category),
            check.SuccessCriteria,
            check.RequiredForLaunch,
            check.OwnerUserId,
            check.DueDate,
            IsOverdue(check, today),
            check.Notes,
            check.Status,
            DescribeStatus(check.Status),
            check.HasOpenBlockers,
            check.BlocksLaunch,
            [.. check.Blockers
                .OrderByDescending(blocker => blocker.CreatedAtUtc)
                .Select(blocker => blocker.ToResponse())],
            check.Version);
    }

    public static ReadinessCheckDetailResponse ToDetailResponse(
        this CampaignReadinessCheck check, DateOnly today, IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(check);

        return new ReadinessCheckDetailResponse(
            check.Id,
            check.CampaignId,
            check.CheckName,
            check.Description,
            check.Category,
            DescribeCategory(check.Category),
            check.SuccessCriteria,
            check.RequiredForLaunch,
            check.OwnerUserId,
            check.DueDate,
            IsOverdue(check, today),
            check.Notes,
            check.Status,
            DescribeStatus(check.Status),
            check.BlocksLaunch,
            check.CreatedAtUtc,
            check.CreatedByUserId,
            check.UpdatedAtUtc,
            check.UpdatedByUserId,
            check.Version,
            [.. check.Blockers.Select(blocker => blocker.ToResponse())],
            permittedActions);
    }

    public static ReadinessBlockerResponse ToResponse(this CampaignReadinessBlocker blocker)
    {
        ArgumentNullException.ThrowIfNull(blocker);

        return new ReadinessBlockerResponse(
            blocker.Id,
            blocker.OwnerUserId,
            blocker.BlockerNote,
            blocker.IsResolved,
            blocker.ResolvedByUserId,
            blocker.ResolvedAtUtc,
            blocker.ResolutionNote,
            blocker.CreatedAtUtc);
    }

    /// <summary>
    /// The whole checklist, with the launch verdict.
    ///
    /// THE PERCENTAGE COUNTS EVERY CHECK while the VERDICT counts only the required ones, and
    /// the difference is deliberate. A checklist at 80% with every required item passed can
    /// launch; one at 95% with a single required payment check outstanding cannot. Showing only
    /// the percentage would make the second look almost ready.
    /// </summary>
    public static CampaignReadinessResponse ToReadinessResponse(
        Campaign campaign, IReadOnlyList<CampaignReadinessCheck> checks, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(checks);

        var passed = checks.Count(check => check.Status == ReadinessCheckStatus.Passed);
        var failed = checks.Count(check => check.Status == ReadinessCheckStatus.Failed);
        var pending = checks.Count(check => check.Status == ReadinessCheckStatus.Pending);
        var requiredOutstanding = checks.Count(check => check.BlocksLaunch);
        var openBlockers = checks.Sum(check => check.Blockers.Count(blocker => !blocker.IsResolved));

        var percentage = checks.Count == 0
            ? 0m
            : Math.Round(passed * 100m / checks.Count, 1);

        return new CampaignReadinessResponse(
            campaign.Id,
            campaign.Code,
            campaign.Name,
            campaign.Status,
            checks.Count,
            passed,
            failed,
            pending,
            requiredOutstanding,
            openBlockers,
            percentage,

            // AN EMPTY CHECKLIST DOES NOT COUNT AS READY. A campaign nobody has written checks
            // for has not been verified, it has merely not been examined - and treating that as
            // a pass is how a campaign launches with no payment configuration.
            CanLaunch: checks.Count > 0 && requiredOutstanding == 0 && openBlockers == 0,

            [.. checks.Select(check => check.ToListItemResponse(today))]);
    }

    public static string DescribeStatus(ReadinessCheckStatus status) => status switch
    {
        ReadinessCheckStatus.Pending => "Pending - not yet verified",
        ReadinessCheckStatus.Passed => "Passed",
        ReadinessCheckStatus.Failed => "Failed - needs attention",
        _ => status.ToString()
    };

    public static string DescribeCategory(ReadinessCheckCategory category) => category switch
    {
        ReadinessCheckCategory.Content => "Content and creative",
        ReadinessCheckCategory.Budget => "Budget and targets",
        ReadinessCheckCategory.Tracking => "Tracking and attribution",
        ReadinessCheckCategory.Payment => "Payment configuration",
        ReadinessCheckCategory.Template => "Templates and receipts",
        ReadinessCheckCategory.Consent => "Consent and compliance",
        _ => category.ToString()
    };

    /// <summary>What the caller may do to this check next.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        CampaignReadinessCheck check, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.ReadinessView))
        {
            actions.Add("View");
        }

        // Only a Pending check may be EDITED. Changing what a check asks for after somebody has
        // judged it would rewrite the question the answer was given to.
        if (check.Status == ReadinessCheckStatus.Pending && hasPermission(PermissionCodes.ReadinessEdit))
        {
            actions.Add("Edit");
        }

        // A VERDICT MAY BE RECORDED ON ANYTHING NOT ALREADY PASSED, which is Pending and Failed.
        //
        // This offered Pass on Pending alone, and a check failed without a blocker on it then had
        // no route back: not Edit (Pending only), not Pass (Pending only), and no blocker to
        // resolve. Its only listed action was AddBlocker - raise an obstacle that does not exist
        // in order to clear it again. Failing a check is what somebody does when the verification
        // has not been done yet, so passing it once it HAS been done is the next ordinary step.
        //
        // Passing is still blocked while a blocker is open: signing off a check somebody has
        // flagged as blocked is exactly what the blocker exists to prevent.
        if (check.Status != ReadinessCheckStatus.Passed)
        {
            if (hasPermission(PermissionCodes.ReadinessPass) && !check.HasOpenBlockers)
            {
                actions.Add("Pass");
            }

            if (check.Status != ReadinessCheckStatus.Failed && hasPermission(PermissionCodes.ReadinessFail))
            {
                actions.Add("Fail");
            }
        }

        if (hasPermission(PermissionCodes.ReadinessManageBlockers))
        {
            if (!check.HasOpenBlockers)
            {
                actions.Add("AddBlocker");
            }
            else
            {
                actions.Add("ResolveBlocker");
            }
        }

        return actions;
    }

    /// <summary>
    /// Whether the check is past its due date and still unresolved.
    ///
    /// A PASSED check is never overdue, however late it was signed off - the work is done, and
    /// flagging it red forever would train people to ignore the colour.
    /// </summary>
    private static bool IsOverdue(CampaignReadinessCheck check, DateOnly today) =>
        check.DueDate.HasValue
        && check.DueDate.Value < today
        && check.Status != ReadinessCheckStatus.Passed;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
