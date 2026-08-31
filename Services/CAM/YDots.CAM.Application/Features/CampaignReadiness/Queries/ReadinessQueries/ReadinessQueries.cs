using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;

namespace YDots.CAM.Application.Features.CampaignReadiness.Queries.ReadinessQueries;

/// <summary>The whole checklist for one campaign, with its launch verdict.</summary>
public sealed record GetCampaignReadinessQuery(Guid CampaignId);

/// <summary>One check in full, with its blockers.</summary>
public sealed record GetReadinessCheckQuery(Guid CheckId);

/// <summary>The read side of the Campaign Readiness slice.</summary>
public sealed class ReadinessQueryHandler(
    ICampaignReadinessReadService readService,
    ICurrentUser currentUser)
{
    public async Task<Result<CampaignReadinessResponse>> HandleAsync(
        GetCampaignReadinessQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var readiness = await readService.GetForCampaignAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        return readiness is null
            ? Result.Failure<CampaignReadinessResponse>(Error.NotFound("That campaign was not found."))
            : Result.Success(readiness);
    }

    public async Task<Result<ReadinessCheckDetailResponse>> HandleAsync(
        GetReadinessCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var check = await readService.GetCheckAsync(query.CheckId, currentUser.Scope, cancellationToken);

        return check is null
            ? Result.Failure<ReadinessCheckDetailResponse>(
                Error.NotFound("That readiness check was not found."))
            : Result.Success(check);
    }
}
