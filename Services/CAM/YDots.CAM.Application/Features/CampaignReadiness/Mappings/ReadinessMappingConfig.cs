using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.CampaignReadiness.Mappings;

/// <summary>Manual mapping for the Campaign Readiness slice.</summary>
public static class ReadinessMappingConfig
{
    /// <summary>Nobody resolved. Lets every mapping below take a dictionary without a null check.</summary>
    private static readonly IReadOnlyDictionary<Guid, PersonSummary> NoPeople =
        new Dictionary<Guid, PersonSummary>();

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
        this CampaignReadinessCheck check,
        DateOnly today,
        IReadOnlyDictionary<Guid, PersonSummary>? people = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        var resolved = people ?? NoPeople;

        return new ReadinessCheckListItemResponse(
            check.Id,
            check.CheckName,
            check.Description,
            check.Category,
            DescribeCategory(check.Category),
            check.SuccessCriteria,
            check.RequiredForLaunch,
            check.OwnerUserId,
            ToPerson(check.OwnerUserId, resolved),
            check.DueDate,
            IsOverdue(check, today),
            check.Notes,
            check.Status,
            DescribeStatus(check.Status),
            check.HasOpenBlockers,
            check.BlocksLaunch,
            [.. check.Blockers
                .OrderByDescending(blocker => blocker.CreatedAtUtc)
                .Select(blocker => blocker.ToResponse(resolved))],
            check.Version);
    }

    public static ReadinessCheckDetailResponse ToDetailResponse(
        this CampaignReadinessCheck check,
        DateOnly today,
        IReadOnlyList<string> permittedActions,
        IReadOnlyDictionary<Guid, PersonSummary>? people = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        var resolved = people ?? NoPeople;

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
            ToPerson(check.OwnerUserId, resolved),
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
            [.. check.Blockers.Select(blocker => blocker.ToResponse(resolved))],
            permittedActions);
    }

    public static ReadinessBlockerResponse ToResponse(
        this CampaignReadinessBlocker blocker,
        IReadOnlyDictionary<Guid, PersonSummary>? people = null)
    {
        ArgumentNullException.ThrowIfNull(blocker);

        return new ReadinessBlockerResponse(
            blocker.Id,
            blocker.OwnerUserId,
            ToPerson(blocker.OwnerUserId, people ?? NoPeople),
            blocker.BlockerNote,
            blocker.IsResolved,
            blocker.ResolvedByUserId,
            blocker.ResolvedAtUtc,
            blocker.ResolutionNote,
            blocker.CreatedAtUtc);
    }

    /// <summary>
    /// The whole checklist, with the launch verdict and the screen's Actions menu.
    ///
    /// THE PERCENTAGE COUNTS EVERY CHECK while the VERDICT counts only the required ones, and
    /// the difference is deliberate. A checklist at 80% with every required item passed can
    /// launch; one at 95% with a single required payment check outstanding cannot. Showing only
    /// the percentage would make the second look almost ready.
    /// </summary>
    public static CampaignReadinessResponse ToReadinessResponse(
        Campaign campaign,
        IReadOnlyList<CampaignReadinessCheck> checks,
        DateOnly today,
        IReadOnlyList<string> permittedActions,
        IReadOnlyDictionary<Guid, PersonSummary>? people = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(checks);

        var resolved = people ?? NoPeople;

        var passed = checks.Count(check => check.Status == ReadinessCheckStatus.Passed);
        var failed = checks.Count(check => check.Status == ReadinessCheckStatus.Failed);
        var pending = checks.Count(check => check.Status == ReadinessCheckStatus.Pending);
        var requiredOutstanding = checks.Count(check => check.BlocksLaunch);
        var openBlockers = checks.Sum(check => check.Blockers.Count(blocker => !blocker.IsResolved));

        var percentage = checks.Count == 0
            ? 0m
            : Math.Round(passed * 100m / checks.Count, 1);

        // A SCHEDULED, AUTO-ACTIVATING CAMPAIGN GOES LIVE WHATEVER THE CHECKLIST SAYS. Saying so
        // on the response is what stops the screen implying otherwise: a red checklist beside a
        // campaign the sweep is going to activate on Tuesday reads as "this is being held back",
        // and it is not.
        //
        // A campaign set to activate MANUALLY is a different story and this is correctly false for
        // it - it is scheduled, but somebody still has to press Activate, so the checklist is
        // genuinely worth clearing first.
        var willActivateAutomatically =
            campaign.Status == CampaignStatus.Scheduled
            && campaign.LifecycleActivation == LifecycleActivation.Auto
            && campaign.EndDate >= today;

        return new CampaignReadinessResponse(
            campaign.Id,
            campaign.Code,
            campaign.Name,
            campaign.Status,
            Campaigns.Mappings.CampaignMappingConfig.DescribeStatus(campaign.Status),
            // The empty-guid guard is not theoretical hygiene: ToPerson answers null for one, and
            // a null sitting in a list the client iterates is a crash on the screen rather than a
            // missing name. Owners are written filtered, so this only catches a legacy row.
            [.. campaign.Owners
                .OrderByDescending(owner => owner.IsPrimary)
                .Select(owner => ToPerson(owner.OwnerId, resolved))
                .OfType<ReadinessPersonResponse>()],
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

            willActivateAutomatically,
            willActivateAutomatically ? campaign.StartDate : null,
            permittedActions,
            [.. checks.Select(check => check.ToListItemResponse(today, resolved))]);
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

        // RAISING AND CLEARING ARE TWO PERMISSIONS NOW, so they are asked separately. They were
        // one code and one if/else, which meant the menu offered whichever half the check's state
        // called for to anybody holding the pair - and holding the pair was the only option.
        if (!check.HasOpenBlockers)
        {
            if (hasPermission(PermissionCodes.ReadinessManageBlockers))
            {
                actions.Add("AddBlocker");
            }
        }
        else if (hasPermission(PermissionCodes.ReadinessResolveBlockers))
        {
            actions.Add("ResolveBlocker");
        }

        // A check nobody has judged yet, with nothing raised against it, can be taken off the
        // list outright. Anything further along carries a verdict or an obstacle that would go
        // with it - see the handler, which refuses both.
        if (check.Status == ReadinessCheckStatus.Pending
            && !check.HasOpenBlockers
            && hasPermission(PermissionCodes.ReadinessDelete))
        {
            actions.Add("Delete");
        }

        return actions;
    }

    /// <summary>
    /// The campaign-level Actions menu on the readiness screen: AddCheck, RequestApproval,
    /// ApproveLaunch, ReturnToDraft.
    ///
    /// AN ORGANISATION ADMINISTRATOR NEVER GETS "REQUEST APPROVAL". They hold every permission in
    /// the Organisation, including the approval, so asking them to raise a request means asking
    /// them to raise a request they are themselves the approver for - and the platform then
    /// refuses it, correctly, as one person doing both halves. The menu they should see has
    /// Approve launch on it and not the request.
    ///
    /// THE RULE IS "CAN THIS CALLER APPROVE", NOT "IS THIS CALLER AN ADMIN". Expressed that way it
    /// covers APPROVER too - a checker looking at somebody else's submitted campaign has no use
    /// for a Request approval button either - and it degrades correctly for anyone else: an
    /// INITIATOR holds submit and not approve, so they see Request approval and no Approve
    /// launch, which is the whole point of the split.
    ///
    /// APPROVE LAUNCH IS GATED ON <c>cam.campaigns.approve</c>, not on a readiness-specific code.
    /// It IS campaign approval, reached from a different screen, and giving it a second code was
    /// what once let somebody refused on the campaigns endpoint approve the same campaign here.
    /// </summary>
    public static IReadOnlyList<string> CampaignActionsFor(
        Campaign campaign, Guid callerUserId, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.ReadinessView))
        {
            actions.Add("View");
        }

        // Checks are added while there is still time to act on them. Past Scheduled the campaign
        // is going live, and a new required check would be one nothing can now clear.
        if (campaign.Status is CampaignStatus.Draft or CampaignStatus.Submitted or CampaignStatus.Approved
            && hasPermission(PermissionCodes.ReadinessCreate))
        {
            actions.Add("AddCheck");
        }

        var canApprove = hasPermission(PermissionCodes.CampaignsApprove);

        if (campaign.Status == CampaignStatus.Draft
            && hasPermission(PermissionCodes.CampaignsSubmit)
            && !canApprove)
        {
            actions.Add("RequestApproval");
        }

        if (campaign.Status == CampaignStatus.Submitted
            && canApprove
            && campaign.CanBeApprovedBy(callerUserId))
        {
            actions.Add("ApproveLaunch");
        }

        // Sending it back is the other half of refusing it, and it is available from every state
        // an approval decision can be pending in - including Scheduled, which approval now
        // produces and which a returned campaign has to be able to leave.
        if (campaign.Status is CampaignStatus.Submitted or CampaignStatus.Approved or CampaignStatus.Scheduled
            && hasPermission(PermissionCodes.ReadinessReturnToDraft))
        {
            actions.Add("ReturnToDraft");
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

    private static ReadinessPersonResponse? ToPerson(
        Guid? userId, IReadOnlyDictionary<Guid, PersonSummary> people)
    {
        if (userId is null || userId == Guid.Empty)
        {
            return null;
        }

        return people.TryGetValue(userId.Value, out var person)
            ? new ReadinessPersonResponse(person.UserId, person.UserCode, person.DisplayName)
            : new ReadinessPersonResponse(userId.Value, null, null);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
