using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.ReferenceData.Queries.GetReferenceData;

/// <summary>GET /api/v1/donors/reference-data. Every catalogue the section's selectors need.</summary>
public sealed record GetReferenceDataQuery;

/// <summary>GET /api/v1/donors/reference-data/campaigns. Scope-aware campaign autocomplete.</summary>
public sealed record SearchCampaignsQuery(string? Search, int MaximumRows);

/// <summary>GET /api/v1/donors/reference-data/leads. Scope-aware lead autocomplete.</summary>
public sealed record SearchLeadsQuery(string? Search, int MaximumRows);

/// <summary>One campaign row for a selector.</summary>
public sealed record CampaignLookupResponse(
    Guid Id,
    string Code,
    string Name,
    string Status,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc);

/// <summary>Every enum catalogue the eight screens draw from, in one call.</summary>
public sealed record ReferenceDataResponse(
    IReadOnlyList<LookupItem> DonorTypes,
    IReadOnlyList<LookupItem> DonorStatuses,
    IReadOnlyList<LookupItem> ApprovalStates,
    IReadOnlyList<LookupItem> LeadStatuses,
    IReadOnlyList<LookupItem> SlaStates,
    IReadOnlyList<LookupItem> ContactOutcomes,
    IReadOnlyList<LookupItem> ConsentChannels,
    IReadOnlyList<LookupItem> ConsentStates,
    IReadOnlyList<LookupItem> ConsentStatuses,
    IReadOnlyList<LookupItem> ContactChannels,
    IReadOnlyList<LookupItem> InteractionTypes,
    IReadOnlyList<LookupItem> MergeDecisions,
    IReadOnlyList<LookupItem> MergeCaseStatuses,
    IReadOnlyList<LookupItem> IdentityConfidences,
    IReadOnlyList<LookupItem> VerificationChannels,
    IReadOnlyList<LookupItem> VerificationStatuses,
    IReadOnlyList<LookupItem> FollowUpStatuses,
    IReadOnlyList<LookupItem> FollowUpPriorities,
    IReadOnlyList<LookupItem> WorkloadBands,
    IReadOnlyList<LookupItem> CampaignStatuses,
    IReadOnlyList<LookupItem> DonationStages,
    IReadOnlyList<LookupItem> PromiseStatuses,
    IReadOnlyList<LookupItem> DocumentClassifications,
    IReadOnlyList<LookupItem> Languages);

/// <summary>
/// Serves the catalogues. Everything comes from the enums themselves, so a value added to an
/// enum appears in the UI without anybody remembering to update a list somewhere.
/// </summary>
public sealed class ReferenceDataQueryHandler(
    ICampaignRepository campaignRepository,
    ILeadRepository leadRepository,
    ICurrentUser currentUser)
{
    public Task<Result<ReferenceDataResponse>> HandleAsync(
        GetReferenceDataQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        _ = cancellationToken;

        var response = new ReferenceDataResponse(
            ToLookup<DonorType>(),
            ToLookup<DonorStatus>(),
            ToLookup<ApprovalState>(),
            ToLookup<LeadStatus>(),
            ToLookup<SlaState>(),
            ToLookup<ContactOutcome>(),
            ToLookup<ConsentChannel>(),
            ToLookup<ConsentState>(),
            ToLookup<ConsentStatus>(),
            ToLookup<ContactChannel>(),
            ToLookup<InteractionType>(),
            ToLookup<MergeDecision>(),
            ToLookup<DonorMergeCaseStatus>(),
            ToLookup<IdentityConfidence>(),
            ToLookup<VerificationChannel>(),
            ToLookup<VerificationStatus>(),
            ToLookup<FollowUpStatus>(),
            ToLookup<FollowUpPriority>(),
            ToLookup<WorkloadBand>(),
            ToLookup<CampaignStatus>(),
            ToLookup<DonationStage>(),
            ToLookup<PromiseStatus>(),
            ToLookup<DocumentClassification>(),
            SupportedLanguages.All);

        return Task.FromResult(Result.Success(response));
    }

    public async Task<Result<IReadOnlyList<CampaignLookupResponse>>> HandleAsync(
        SearchCampaignsQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = query.MaximumRows is <= 0 or > 50 ? 20 : query.MaximumRows;

        var campaigns = await campaignRepository.SearchAsync(
            currentUser.OrganisationId, query.Search, rows, cancellationToken);

        IReadOnlyList<CampaignLookupResponse> items =
        [
            .. campaigns.Select(campaign => new CampaignLookupResponse(
                campaign.Id, campaign.Code, campaign.Name, campaign.Status.ToString(),
                campaign.StartsAtUtc, campaign.EndsAtUtc))
        ];

        return Result.Success(items);
    }

    public async Task<Result<IReadOnlyList<LeadLookupResponse>>> HandleAsync(
        SearchLeadsQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = new DTOs.LeadSearchFilter
        {
            Search = query.Search,
            Page = 1,
            PageSize = query.MaximumRows is <= 0 or > 50 ? 20 : query.MaximumRows
        };

        var page = await leadRepository.SearchAsync(filter, currentUser.Scope, cancellationToken);

        IReadOnlyList<LeadLookupResponse> items = [.. page.Items.Select(lead => lead.ToLookupResponse())];

        return Result.Success(items);
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
