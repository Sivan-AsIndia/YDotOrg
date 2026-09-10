using Microsoft.Extensions.Logging;
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
    ICurrentUser currentUser,
    ILogger<ReadinessQueryHandler> logger)
{
    public async Task<Result<CampaignReadinessResponse>> HandleAsync(
        GetCampaignReadinessQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign readiness. CampaignId: {CampaignId}.",
            query.CampaignId);

        var readiness = await readService.GetForCampaignAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        if (readiness is null)
        {
            logger.LogWarning("Campaign readiness not found. CampaignId: {CampaignId}.",
                query.CampaignId);

            return Result.Failure<CampaignReadinessResponse>(
                Error.NotFound("That campaign was not found."));
        }

        logger.LogInformation("Campaign readiness retrieved. CampaignId: {CampaignId}.",
            query.CampaignId);

        return Result.Success(readiness);
    }

    public async Task<Result<ReadinessCheckDetailResponse>> HandleAsync(
        GetReadinessCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving readiness check. CheckId: {CheckId}.",
            query.CheckId);

        var check = await readService.GetCheckAsync(
            query.CheckId, currentUser.Scope, cancellationToken);

        if (check is null)
        {
            logger.LogWarning("Readiness check not found. CheckId: {CheckId}.",
                query.CheckId);

            return Result.Failure<ReadinessCheckDetailResponse>(
                Error.NotFound("That readiness check was not found."));
        }

        logger.LogInformation("Readiness check retrieved. CheckId: {CheckId}.",
            query.CheckId);

        return Result.Success(check);
    }
}