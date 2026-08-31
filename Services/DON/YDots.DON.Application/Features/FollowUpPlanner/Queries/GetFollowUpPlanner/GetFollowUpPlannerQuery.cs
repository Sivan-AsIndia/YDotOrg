using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.DTOs;
using YDots.DON.Application.Features.FollowUpPlanner.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.FollowUpPlanner.Queries.GetFollowUpPlanner;

/// <summary>DON-UI-08 GET list.</summary>
public sealed record GetFollowUpPlannerQuery(FollowUpSearchFilter Filter);

/// <summary>DON-UI-08 GET one.</summary>
public sealed record GetFollowUpDetailQuery(Guid FollowUpId);

/// <summary>
/// GET the consent warning for a donor before anything is scheduled. The screen calls this as
/// soon as a donor is selected, which is what lets it show the warning before the person has
/// filled in the rest of the form.
/// </summary>
public sealed record GetConsentWarningQuery(Guid? DonorId, Guid? LeadId);

public sealed class FollowUpPlannerQueryHandler(
    IFollowUpRepository followUpRepository,
    IConsentRepository consentRepository,
    IDonorRepository donorRepository,
    ILeadRepository leadRepository,
    ICurrentUser currentUser,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<FollowUpPlannerResponse>> HandleAsync(
        GetFollowUpPlannerQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;

        if (filter.OnlyMine == true)
        {
            filter.RelationshipOwnerUserId = currentUser.UserId;
        }

        var page = await followUpRepository.SearchAsync(filter, currentUser.Scope, cancellationToken);
        var canSeeContact = currentUser.CanSeeContact();
        var canSeeEvidence = currentUser.CanSeeEvidence();

        var rows = new List<FollowUpResponse>(page.Items.Count);

        foreach (var task in page.Items)
        {
            var warning = await BuildWarningAsync(task.DonorId, task.LeadId, cancellationToken);
            rows.Add(task.ToResponse(canSeeContact, canSeeEvidence, warning));
        }

        var owners = await leadRepository.GetKnownOwnersAsync(currentUser.OrganisationId, cancellationToken);

        var response = new FollowUpPlannerResponse(
            ScreenIds.FollowUpPlanner,
            ScreenRoutes.FollowUpPlanner,
            new PagedResponse<FollowUpResponse>(rows, page.TotalCount, page.Page, page.PageSize),
            ToLookup<ConsentChannel>(),
            ToLookup<FollowUpPriority>(),
            ToLookup<FollowUpStatus>(),
            SupportedLanguages.All,
            [.. owners.Select(owner => new LookupItem(owner.UserId.ToString(), owner.Name, owner.TeamCode))],
            _settings.CurrentNoticeVersion,
            BuildPermittedActions(),
            DescribeFilter(filter),
            DescribeScope(),
            rows.Count == 0 ? ScreenState.Empty : ScreenState.Initial);

        return Result.Success(response);
    }

    public async Task<Result<FollowUpResponse>> HandleAsync(
        GetFollowUpDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var task = await followUpRepository.GetByIdAsync(query.FollowUpId, cancellationToken);

        if (task is null || task.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<FollowUpResponse>(Error.NotFound("That follow-up was not found inside your scope."));
        }

        if (currentUser.Scope.IsOwnRecordsOnly && task.RelationshipOwnerUserId != currentUser.UserId)
        {
            return Result.Failure<FollowUpResponse>(Error.NotFound("That follow-up was not found inside your scope."));
        }

        var warning = await BuildWarningAsync(task.DonorId, task.LeadId, cancellationToken);

        return Result.Success(task.ToResponse(
            currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), warning));
    }

    public async Task<Result<ConsentWarningResponse>> HandleAsync(
        GetConsentWarningQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DonorId is null && query.LeadId is null)
        {
            return Result.Failure<ConsentWarningResponse>(Error.Validation(
                "Enter Donor or lead reference before the consent warning can be checked."));
        }

        var warning = await BuildWarningAsync(query.DonorId, query.LeadId, cancellationToken);

        return Result.Success(warning);
    }

    private async Task<ConsentWarningResponse> BuildWarningAsync(
        Guid? donorId,
        Guid? leadId,
        CancellationToken cancellationToken)
    {
        Donor? donor = null;
        IReadOnlyList<Consent> consents = [];

        if (donorId is not null)
        {
            donor = await donorRepository.GetByIdAsync(donorId.Value, cancellationToken);
            consents = await consentRepository.GetCurrentForDonorAsync(donorId.Value, cancellationToken);
        }
        else if (leadId is not null)
        {
            consents = await consentRepository.GetForLeadAsync(leadId.Value, cancellationToken);
        }

        return FollowUpMappingConfig.BuildConsentWarning(donor, consents);
    }

    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string> { "View" };

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerSchedule))
        {
            actions.Insert(0, "Schedule follow-up");
        }

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerAssign))
        {
            actions.Add("Assign");
        }

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerMarkComplete))
        {
            actions.Add("Mark complete");
        }

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerReschedule))
        {
            actions.Add("Reschedule");
        }

        if (currentUser.HasPermission(PermissionCodes.FollowUpPlannerCancelTask))
        {
            actions.Add("Cancel task");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    private static string DescribeFilter(FollowUpSearchFilter filter)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            parts.Add($"search '{filter.Search}'");
        }

        if (filter.DonorId is not null)
        {
            parts.Add("donor filter");
        }

        if (filter.LeadId is not null)
        {
            parts.Add("lead filter");
        }

        if (filter.Status is not null)
        {
            parts.Add($"status {filter.Status}");
        }

        if (filter.Priority is not null)
        {
            parts.Add($"priority {filter.Priority}");
        }

        if (filter.PermittedChannel is not null)
        {
            parts.Add($"channel {filter.PermittedChannel}");
        }

        if (filter.DueBeforeUtc is not null)
        {
            parts.Add($"due before {filter.DueBeforeUtc:yyyy-MM-dd}");
        }

        if (filter.OnlyMine == true)
        {
            parts.Add("only my tasks");
        }

        return parts.Count == 0 ? "No filters applied." : "Filtered by " + string.Join(", ", parts) + ".";
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
