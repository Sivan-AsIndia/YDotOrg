using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.LeadWorkQueue.DTOs;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.LeadWorkQueue.Queries.GetLeadWorkQueue;

/// <summary>SCR-DON-001 GET. Prioritise new, due, overdue and nurture leads.</summary>
public sealed record GetLeadWorkQueueQuery(LeadSearchFilter Filter);

/// <summary>GET one lead from the queue, for the detail panel.</summary>
public sealed record GetLeadDetailQuery(Guid LeadId);

public sealed class LeadWorkQueueQueryHandler(
    ILeadRepository leadRepository,
    ICampaignRepository campaignRepository,
    IConsentRepository consentRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<LeadWorkQueueResponse>> HandleAsync(
        GetLeadWorkQueueQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;

        if (filter.OnlyMine == true)
        {
            filter.OwnerUserId = currentUser.UserId;
        }

        var page = await leadRepository.SearchAsync(filter, currentUser.Scope, cancellationToken);
        var now = clock.UtcNow;
        var canSeeContact = currentUser.CanSeeContact();

        // The SLA badge is recalculated on read. Overdue happens because time passed, not
        // because somebody saved the record, so a stored value would be wrong most of the day.
        foreach (var lead in page.Items)
        {
            lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, now, _settings);
        }

        var rows = page.Items.Select(lead => lead.ToListItemResponse(canSeeContact, now)).ToList();

        var campaigns = await campaignRepository.GetActiveAsync(currentUser.OrganisationId, cancellationToken);
        var owners = await leadRepository.GetKnownOwnersAsync(currentUser.OrganisationId, cancellationToken);
        var counts = await leadRepository.GetStatusCountsAsync(currentUser.OrganisationId, currentUser.Scope, cancellationToken);
        var summary = await leadRepository.GetQueueSummaryAsync(currentUser.OrganisationId, currentUser.Scope, cancellationToken);

        var response = new LeadWorkQueueResponse(
            ScreenIds.LeadWorkQueue,
            ScreenRoutes.LeadWorkQueue,
            new PagedResponse<LeadListItemResponse>(rows, page.TotalCount, page.Page, page.PageSize),
            [.. campaigns.Select(campaign => new LookupItem(campaign.Id.ToString(), campaign.Name, campaign.Code))],
            [.. owners.Select(owner => new LookupItem(owner.UserId.ToString(), owner.Name, owner.TeamCode))],
            ToLookup<LeadStatus>(),
            ToLookup<SlaState>(),
            SupportedLanguages.All,
            ToLookup<ContactOutcome>(),
            counts,
            summary,
            ToLookup<LeadTemperature>(),
            ToLookup<DonationPotential>(),
            BuildPermittedActions(),
            DescribeFilter(filter),
            DescribeScope(),
            now,
            rows.Count == 0 ? ScreenState.Empty : ScreenState.Initial);

        return Result.Success(response);
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        GetLeadDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var lead = await leadRepository.GetByIdAsync(query.LeadId, cancellationToken);

        if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<LeadDetailResponse>(Error.NotFound("That lead was not found inside your scope."));
        }

        if (currentUser.Scope.IsOwnRecordsOnly && lead.OwnerUserId != currentUser.UserId)
        {
            return Result.Failure<LeadDetailResponse>(Error.NotFound("That lead was not found inside your scope."));
        }

        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);

        return Result.Success(lead.ToDetailResponse(
            currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    /// <summary>
    /// The verbs the queue screen may draw.
    ///
    /// "CREATE" IS A LEAD-CAPTURE RIGHT, NOT A QUEUE ONE, and it is listed here because the
    /// Create Lead button lives on this screen while the form it opens belongs to SCR-DON-002.
    /// Without it the screen had no honest way to ask the question and inferred the answer from
    /// <c>Accept</c>/<c>Contact</c> - two queue verbs that say nothing about whether the caller
    /// can save a lead, so a reader who could pick work up was offered a form the API would
    /// refuse.
    /// </summary>
    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string> { "Filter", "Open" };

        if (currentUser.HasPermission(PermissionCodes.LeadCaptureSave))
        {
            actions.Add("Create");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueAccept))
        {
            actions.Insert(0, "Accept");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueAssign))
        {
            actions.Add("Assign");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueContact))
        {
            actions.Add("Contact");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueQualify))
        {
            actions.Add("Qualify");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadWorkQueueClose))
        {
            actions.Add("Close");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    /// <summary>
    /// The "active filter summary" the screen has to show. Written as plain language rather
    /// than a chip list so it also reads correctly to a screen reader.
    /// </summary>
    private static string DescribeFilter(LeadSearchFilter filter)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            parts.Add($"search '{filter.Search}'");
        }

        if (filter.CampaignId is not null)
        {
            parts.Add("campaign filter");
        }

        if (filter.OwnerUserId is not null)
        {
            parts.Add("owner filter");
        }

        if (filter.Status is not null)
        {
            parts.Add($"status {filter.Status}");
        }

        if (filter.SlaState is not null)
        {
            parts.Add($"SLA {filter.SlaState}");
        }

        if (!string.IsNullOrWhiteSpace(filter.PreferredLanguage))
        {
            parts.Add($"language {filter.PreferredLanguage}");
        }

        if (filter.DueBeforeUtc is not null)
        {
            parts.Add($"due before {filter.DueBeforeUtc:yyyy-MM-dd}");
        }

        if (filter.DueAfterUtc is not null)
        {
            parts.Add($"due after {filter.DueAfterUtc:yyyy-MM-dd}");
        }

        if (filter.OnlyMine == true)
        {
            parts.Add("only my leads");
        }

        return parts.Count == 0 ? "No filters applied." : "Filtered by " + string.Join(", ", parts) + ".";
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
