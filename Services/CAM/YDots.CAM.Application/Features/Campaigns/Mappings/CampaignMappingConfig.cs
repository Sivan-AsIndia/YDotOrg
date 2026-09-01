using System.Globalization;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Campaigns.Mappings;

/// <summary>
/// Manual mapping for the Campaigns slice: request to entity, entity to response.
///
/// THE MAPPER NORMALISES, the validator judges and the handler authorises. Trimming a name and
/// upper-casing a code is none of the other two jobs, and doing it here means an import, a seed
/// and the create screen all produce the same bytes in the column.
///
/// WHAT IS NOT HERE ANY MORE. The old profile had a <c>ToCommand</c> per request type - ten
/// methods whose entire content was copying twenty fields from a class onto an identical
/// record. The commands now carry the request itself, so those disappeared along with the
/// twenty-field copies nobody could review.
/// </summary>
public static class CampaignMappingConfig
{
    /// <summary>
    /// Builds a new Campaign from a create request.
    ///
    /// TenantId, BusinessUnitId, the audit columns and the version are NOT set here.
    /// <c>BaseEntity</c> supplies the Guid and <c>CampaignDbContext.SaveChangesAsync</c> stamps
    /// the rest - a mapper that also set them would be writing values that are about to be
    /// overwritten, and would hide the fact that a caller cannot choose them.
    /// </summary>
    public static Campaign ToEntity(this CreateCampaignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var campaign = new Campaign
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Purpose = request.Purpose.Trim(),
            FundOrProgramme = request.FundOrProgramme.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            // A placeholder, not a figure anybody entered: Target & Budget is on hold and no screen
            // collects either value. The column stays non-nullable so the module can pick it up
            // again without a migration.
            TargetAmount = 0m,
            BudgetAmount = null,
            CurrencyId = request.CurrencyId,
            CountryId = request.CountryId,
            StateId = request.StateId,
            CityId = request.CityId,
            ZipCode = Clean(request.ZipCode),
            LifecycleActivation = request.LifecycleActivation,
            DaysBeforeStart = request.DaysBeforeStart,
            ReminderTime = request.ReminderTime,
            PublicDescription = Clean(request.PublicDescription),
            TermsAndNotice = Clean(request.TermsAndNotice),

            // Only Draft or Submitted reach here; the validator refuses anything further along.
            Status = request.Status
        };

        ApplyOwners(campaign, request.OwnerIds);
        ApplyChannels(campaign, request.ChannelIds);

        return campaign;
    }

    /// <summary>
    /// Applies an update in place.
    ///
    /// STATUS IS NOT TOUCHED, and cannot be: the request has no field for it. Lifecycle
    /// transitions go through their own endpoints, each with its own permission and rules.
    /// </summary>
    public static void ApplyTo(this UpdateCampaignRequest request, Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(campaign);

        campaign.Name = request.Name.Trim();
        campaign.Purpose = request.Purpose.Trim();
        campaign.FundOrProgramme = request.FundOrProgramme.Trim();
        campaign.StartDate = request.StartDate;
        campaign.EndDate = request.EndDate;
        // TargetAmount and BudgetAmount are LEFT ALONE. They are not on the update contract while
        // Target & Budget is on hold, and assigning them from a request that no longer carries them
        // is what silently zeroed a stored target on every edit.
        campaign.CurrencyId = request.CurrencyId;
        campaign.CountryId = request.CountryId;
        campaign.StateId = request.StateId;
        campaign.CityId = request.CityId;
        campaign.ZipCode = Clean(request.ZipCode);
        campaign.LifecycleActivation = request.LifecycleActivation;
        campaign.DaysBeforeStart = request.DaysBeforeStart;
        campaign.ReminderTime = request.ReminderTime;
        campaign.PublicDescription = Clean(request.PublicDescription);
        campaign.TermsAndNotice = Clean(request.TermsAndNotice);

        ApplyOwners(campaign, request.OwnerIds);
        ApplyChannels(campaign, request.ChannelIds);
    }

    /// <summary>
    /// Replaces the owner set.
    ///
    /// THE WHOLE SET IS SENT, NOT A DELTA, which is what makes the screen's Save button mean
    /// what it appears to mean: who is listed is who ends up owning it. Rows that survive the
    /// edit are LEFT ALONE rather than removed and re-added, so their CreatedAt keeps recording
    /// when that person actually became an owner - which is the ownership history the module
    /// brief asks to be auditable.
    /// </summary>
    private static void ApplyOwners(Campaign campaign, IReadOnlyList<Guid>? ownerIds)
    {
        var wanted = (ownerIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

        foreach (var removed in campaign.Owners.Where(o => !wanted.Contains(o.OwnerId)).ToList())
        {
            campaign.Owners.Remove(removed);
        }

        var existing = campaign.Owners.Select(o => o.OwnerId).ToHashSet();

        foreach (var ownerId in wanted.Where(id => !existing.Contains(id)))
        {
            campaign.Owners.Add(new CampaignOwner
            {
                CampaignId = campaign.Id,
                OwnerId = ownerId,

                // The first owner listed is the one of record, so a notification that must reach
                // a single person has somebody to reach.
                IsPrimary = wanted.IndexOf(ownerId) == 0
            });
        }
    }

    /// <summary>Replaces the channel set. A pure join, so removed rows simply go.</summary>
    private static void ApplyChannels(Campaign campaign, IReadOnlyList<Guid>? channelIds)
    {
        var wanted = (channelIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

        foreach (var removed in campaign.Channels.Where(c => !wanted.Contains(c.ChannelId)).ToList())
        {
            campaign.Channels.Remove(removed);
        }

        var existing = campaign.Channels.Select(c => c.ChannelId).ToHashSet();

        foreach (var channelId in wanted.Where(id => !existing.Contains(id)))
        {
            campaign.Channels.Add(new CampaignChannel
            {
                CampaignId = campaign.Id,
                ChannelId = channelId
            });
        }
    }

    /// <summary>One row of the register.</summary>
    /// <param name="ownerIds">
    /// The owners, PROJECTED BY THE READ SERVICE rather than read off the entity.
    ///
    /// The register's query is <c>AsNoTracking()</c> with no <c>Include</c> for the owners, so
    /// <c>campaign.Owners</c> is an empty collection by the time this runs - which is why the
    /// count this used to read from it was 0 on every row of every register, for every campaign,
    /// however many owners it actually had.
    /// </param>
    public static CampaignListItemResponse ToListItemResponse(
        this Campaign campaign,
        DateOnly today,
        IReadOnlyList<Guid> ownerIds,
        int trackingAssetCount,
        int outstandingCheckCount)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(ownerIds);

        return new CampaignListItemResponse(
            campaign.Id,
            campaign.TenantId,
            campaign.Code,
            campaign.Name,
            campaign.FundOrProgramme,
            campaign.StartDate,
            campaign.EndDate,
            campaign.TargetAmount,
            campaign.BudgetAmount,
            campaign.CurrencyId,
            campaign.Status,
            DescribeStatus(campaign.Status),
            ElapsedPercent(campaign, today),
            ownerIds.Count,
            ownerIds,
            trackingAssetCount,
            outstandingCheckCount,
            campaign.UpdatedAtUtc,
            campaign.Version);
    }

    /// <summary>The detail screen.</summary>
    public static CampaignDetailResponse ToDetailResponse(
        this Campaign campaign,
        CampaignLifecycleAction? pendingCloseRequest,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        return new CampaignDetailResponse(
            campaign.Id,
            campaign.TenantId,
            campaign.BusinessUnitId,
            campaign.Code,
            campaign.Name,
            campaign.Purpose,
            campaign.FundOrProgramme,
            campaign.StartDate,
            campaign.EndDate,
            campaign.TargetAmount,
            campaign.CurrencyId,
            campaign.BudgetAmount,
            campaign.CountryId,
            campaign.StateId,
            campaign.CityId,
            campaign.ZipCode,
            campaign.LifecycleActivation,
            campaign.DaysBeforeStart,
            campaign.ReminderTime,
            campaign.PublicDescription,
            campaign.TermsAndNotice,
            campaign.Status,
            DescribeStatus(campaign.Status),
            [.. campaign.Owners.Select(owner => owner.OwnerId)],
            [.. campaign.Channels.Select(channel => channel.ChannelId)],
            campaign.SubmittedByUserId,
            campaign.SubmittedAtUtc,
            campaign.ApprovedByUserId,
            campaign.ApprovedAtUtc,
            campaign.CreatedAtUtc,
            campaign.CreatedByUserId,
            campaign.UpdatedAtUtc,
            campaign.UpdatedByUserId,
            campaign.Version,
            pendingCloseRequest?.ToResponse(),
            permittedActions);
    }

    public static CampaignLifecycleActionResponse ToResponse(this CampaignLifecycleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new CampaignLifecycleActionResponse(
            action.Id,
            action.ActionType,
            DescribeAction(action.ActionType),
            action.ActionStatus,
            action.EffectiveAtUtc,
            action.ReasonCategory,
            action.DetailedReason,
            action.CommunicationImpact,
            action.ClosureSummary,
            action.RequestedByUserId,
            action.ApprovedByUserId,
            action.ApprovedAtUtc,
            action.CreatedAtUtc);
    }

    public static CampaignHistoryResponse ToHistoryResponse(this CampaignAuditEvent audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return new CampaignHistoryResponse(
            audit.Id,
            audit.ActionCode,
            audit.ActorUserId,
            audit.TargetType,
            audit.TargetId,
            audit.Result,
            audit.Reason,
            audit.OccurredAtUtc);
    }

    public static CampaignExportRow ToExportRow(
        this Campaign campaign, int trackingAssetCount)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        return new CampaignExportRow(
            campaign.Code,
            campaign.Name,
            campaign.FundOrProgramme,
            campaign.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            campaign.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            campaign.TargetAmount.ToString(CultureInfo.InvariantCulture),
            campaign.BudgetAmount?.ToString(CultureInfo.InvariantCulture),
            campaign.Status.ToString(),
            campaign.Owners.Count.ToString(CultureInfo.InvariantCulture),
            trackingAssetCount.ToString(CultureInfo.InvariantCulture),
            campaign.UpdatedAtUtc?.ToString("u", CultureInfo.InvariantCulture));
    }

    /// <summary>The human-readable form of a campaign status, for the grid's status chip.</summary>
    public static string DescribeStatus(CampaignStatus status) => status switch
    {
        CampaignStatus.Draft => "Draft - being prepared",
        CampaignStatus.Submitted => "Submitted - awaiting approval",
        CampaignStatus.Approved => "Approved - not yet live",
        CampaignStatus.Scheduled => "Scheduled - goes live on its start date",
        CampaignStatus.Active => "Active - accepting donations",
        CampaignStatus.Paused => "Paused - temporarily not accepting donations",
        CampaignStatus.Closing => "Closing - awaiting close approval",
        CampaignStatus.Closed => "Closed",
        CampaignStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string DescribeAction(CampaignLifecycleActionType actionType) => actionType switch
    {
        CampaignLifecycleActionType.RequestClose => "Close requested",
        CampaignLifecycleActionType.ApproveClose => "Close approved",
        CampaignLifecycleActionType.CancelDraft => "Draft cancelled",
        _ => actionType.ToString()
    };

    /// <summary>
    /// What the caller may do to this campaign next.
    ///
    /// IT COMBINES THREE THINGS, and all three have to hold: the campaign's STATE allows the
    /// transition, the caller HOLDS the permission, and for the approvals the caller is
    /// INDEPENDENT of the person who created or submitted it. Deciding it here rather than in
    /// the client is what stops a screen drawing an Approve button that will answer 409.
    ///
    /// <paramref name="hasOutstandingChecks"/> removes Activate while a required readiness
    /// check has not passed, which is the checklist doing its job rather than the launch
    /// failing at the last step.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        Campaign campaign,
        Guid callerUserId,
        Func<string, bool> hasPermission,
        bool hasOutstandingChecks,
        bool hasPendingCloseRequest)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.CampaignsView))
        {
            actions.Add("View");
        }

        if (hasPermission(PermissionCodes.CampaignsViewHistory))
        {
            actions.Add("ViewHistory");
        }

        if (hasPermission(PermissionCodes.CampaignsExport))
        {
            actions.Add("Export");
        }

        // Only a Draft may be freely edited. Past that the campaign has been seen by an
        // approver, and changing its target or dates underneath them would make the approval
        // meaningless.
        if (campaign.Status == CampaignStatus.Draft)
        {
            if (hasPermission(PermissionCodes.CampaignsEdit))
            {
                actions.Add("Edit");
            }

            if (hasPermission(PermissionCodes.CampaignsSubmit))
            {
                actions.Add("Submit");
            }

            if (hasPermission(PermissionCodes.CampaignsDeleteDraft))
            {
                actions.Add("Delete");
            }
        }

        if (campaign.Status == CampaignStatus.Submitted
            && hasPermission(PermissionCodes.CampaignsApprove)
            && campaign.CanBeApprovedBy(callerUserId))
        {
            actions.Add("Approve");
        }

        if (campaign.Status is CampaignStatus.Approved or CampaignStatus.Scheduled
            && hasPermission(PermissionCodes.CampaignsActivate)
            && !hasOutstandingChecks)
        {
            actions.Add("Activate");
        }

        if (campaign.Status == CampaignStatus.Active && hasPermission(PermissionCodes.CampaignsPause))
        {
            actions.Add("Pause");
        }

        if (campaign.Status == CampaignStatus.Paused && hasPermission(PermissionCodes.CampaignsResume))
        {
            actions.Add("Resume");
        }

        if (campaign.Status is CampaignStatus.Active or CampaignStatus.Paused
            && !hasPendingCloseRequest
            && hasPermission(PermissionCodes.CampaignsRequestClose))
        {
            actions.Add("RequestClose");
        }

        // The approver of a close request must not be the person who raised it. Whether THIS
        // caller raised it is decided against the request row itself by the handler; the button
        // is offered here whenever one is pending and the caller holds the permission.
        if (hasPendingCloseRequest && hasPermission(PermissionCodes.CampaignsApproveClose))
        {
            actions.Add("ApproveClose");
        }

        return actions;
    }

    /// <summary>
    /// How far through its own dates the campaign is, 0 to 100.
    ///
    /// Null before it starts, because "0% elapsed" and "has not begun" are different things and
    /// a progress bar should show nothing rather than an empty bar for the second.
    /// </summary>
    private static int? ElapsedPercent(Campaign campaign, DateOnly today)
    {
        if (today < campaign.StartDate)
        {
            return null;
        }

        if (today >= campaign.EndDate)
        {
            return 100;
        }

        var total = campaign.EndDate.DayNumber - campaign.StartDate.DayNumber;

        // A same-day campaign has no span to divide by, and on its one day it is fully elapsed.
        return total <= 0 ? 100 : (int)Math.Round((today.DayNumber - campaign.StartDate.DayNumber) * 100.0 / total);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
