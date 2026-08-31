using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.DuplicateReview.DTOs;
using YDots.DON.Application.Features.DuplicateReview.Mappings;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.DuplicateReview.Queries.GetDuplicateReview;

/// <summary>SCR-DON-004 GET list. Compare candidates and decide link, merge or keep separate.</summary>
public sealed record GetDuplicateReviewListQuery(DuplicateReviewSearchFilter Filter);

/// <summary>SCR-DON-004 GET one. The full evidence panel for a single review.</summary>
public sealed record GetDuplicateReviewDetailQuery(Guid ReviewId);

public sealed class DuplicateReviewQueryHandler(
    IDonorMergeCaseRepository mergeCaseRepository,
    ICurrentUser currentUser)
{
    public async Task<Result<DuplicateReviewListResponse>> HandleAsync(
        GetDuplicateReviewListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await mergeCaseRepository.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);
        var rows = page.Items.Select(item => item.ToListItemResponse()).ToList();

        var response = new DuplicateReviewListResponse(
            ScreenIds.DuplicateReview,
            ScreenRoutes.DuplicateReview,
            new PagedResponse<DuplicateReviewListItemResponse>(rows, page.TotalCount, page.Page, page.PageSize),
            ToLookup<DonorMergeCaseStatus>(),
            ToLookup<IdentityConfidence>(),
            ToLookup<MergeDecision>(),
            BuildPermittedActions(),
            DescribeFilter(query.Filter),
            DescribeScope(),
            rows.Count == 0 ? ScreenState.Empty : ScreenState.Initial);

        return Result.Success(response);
    }

    public async Task<Result<DuplicateReviewDetailResponse>> HandleAsync(
        GetDuplicateReviewDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var mergeCase = await mergeCaseRepository.GetWithCandidatesAsync(query.ReviewId, cancellationToken);

        if (mergeCase is null || mergeCase.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<DuplicateReviewDetailResponse>(
                Error.NotFound("That duplicate review was not found inside your scope."));
        }

        return Result.Success(mergeCase.ToDetailResponse(
            currentUser.CanSeeContact(),
            currentUser.CanSeeEvidence(),
            DuplicateReviewMappingConfig.PermittedActionsFor(mergeCase)));
    }

    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string> { "Review evidence" };

        if (currentUser.HasPermission(PermissionCodes.DuplicateReviewMerge))
        {
            actions.Add("Merge");
        }

        if (currentUser.HasPermission(PermissionCodes.DuplicateReviewRejectCandidate))
        {
            actions.Add("Reject candidate");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    private static string DescribeFilter(DuplicateReviewSearchFilter filter)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            parts.Add($"search '{filter.Search}'");
        }

        if (filter.Status is not null)
        {
            parts.Add($"status {filter.Status}");
        }

        if (filter.IdentityConfidence is not null)
        {
            parts.Add($"confidence {filter.IdentityConfidence}");
        }

        if (filter.Decision is not null)
        {
            parts.Add($"decision {filter.Decision}");
        }

        if (filter.CandidateDonorId is not null)
        {
            parts.Add("candidate filter");
        }

        return parts.Count == 0 ? "No filters applied." : "Filtered by " + string.Join(", ", parts) + ".";
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
