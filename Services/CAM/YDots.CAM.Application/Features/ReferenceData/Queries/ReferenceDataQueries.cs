using Microsoft.Extensions.Logging;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.ReferenceData.DTOs;
using YDots.CAM.Application.Features.ReferenceData.Mappings;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.ReferenceData.Queries;

/// <summary>Every dropdown the campaign screens need, in one call.</summary>
public sealed record GetCampaignReferenceDataQuery(bool ActiveOnly = true);

/// <summary>The channel list on its own, for a screen that needs only that.</summary>
public sealed record GetChannelsQuery(bool ActiveOnly = true);

/// <summary>The source list on its own.</summary>
public sealed record GetSourcesQuery(bool ActiveOnly = true);

/// <summary>The medium list on its own.</summary>
public sealed record GetMediumsQuery(bool ActiveOnly = true);

/// <summary>
/// The read side of the reference tables.
///
/// ONE HANDLER REPLACING THREE - GetChannelsQueryHandler, GetSourcesQueryHandler and
/// GetMediumsQueryHandler were the same six lines against three tables.
/// </summary>
public sealed class ReferenceDataQueryHandler(
    IReferenceDataRepository referenceData,
    ILogger<ReferenceDataQueryHandler> logger)
{
    public async Task<Result<CampaignReferenceDataResponse>> HandleAsync(
        GetCampaignReferenceDataQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign reference data. ActiveOnly: {ActiveOnly}.",
            query.ActiveOnly);

        // FETCHED ONE AFTER ANOTHER, NOT IN PARALLEL. The three reads share one DbContext, and
        // EF Core permits exactly one operation on a context at a time - starting them together
        // throws "a second operation was started on this context instance". Nor was there
        // anything to win: one context means one connection, so the database would have
        // serialised them anyway.
        var channels = await referenceData.GetChannelsAsync(query.ActiveOnly, cancellationToken);
        var sources = await referenceData.GetSourcesAsync(query.ActiveOnly, cancellationToken);
        var mediums = await referenceData.GetMediumsAsync(query.ActiveOnly, cancellationToken);

        logger.LogInformation("Campaign reference data retrieved. Channels: {ChannelCount}, Sources: {SourceCount}, Mediums: {MediumCount}.",
            channels.Count, sources.Count, mediums.Count);

        return Result.Success(new CampaignReferenceDataResponse(
            [.. channels.Select(channel => channel.ToResponse())],
            [.. sources.Select(source => source.ToResponse())],
            [.. mediums.Select(medium => medium.ToResponse())],
            ReferenceDataMappingConfig.Describe<CampaignStatus>(),
            ReferenceDataMappingConfig.Describe<LifecycleActivation>(),
            ReferenceDataMappingConfig.Describe<TrackingAssetType>(),
            ReferenceDataMappingConfig.Describe<TrackingAssetStatus>(),
            ReferenceDataMappingConfig.Describe<ReadinessCheckCategory>(),
            ReferenceDataMappingConfig.Describe<ReadinessCheckStatus>()));
    }

    public async Task<Result<IReadOnlyList<ReferenceItemResponse>>> HandleAsync(
        GetChannelsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign channels. ActiveOnly: {ActiveOnly}.",
            query.ActiveOnly);

        var channels = await referenceData.GetChannelsAsync(query.ActiveOnly, cancellationToken);

        logger.LogInformation("Campaign channels retrieved. Count: {ChannelCount}.",
            channels.Count);

        return Result.Success<IReadOnlyList<ReferenceItemResponse>>(
            [.. channels.Select(channel => channel.ToResponse())]);
    }

    public async Task<Result<IReadOnlyList<ReferenceItemResponse>>> HandleAsync(
        GetSourcesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign sources. ActiveOnly: {ActiveOnly}.",
            query.ActiveOnly);

        var sources = await referenceData.GetSourcesAsync(query.ActiveOnly, cancellationToken);

        logger.LogInformation("Campaign sources retrieved. Count: {SourceCount}.",
            sources.Count);

        return Result.Success<IReadOnlyList<ReferenceItemResponse>>(
            [.. sources.Select(source => source.ToResponse())]);
    }

    public async Task<Result<IReadOnlyList<ReferenceItemResponse>>> HandleAsync(
        GetMediumsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving campaign mediums. ActiveOnly: {ActiveOnly}.",
            query.ActiveOnly);

        var mediums = await referenceData.GetMediumsAsync(query.ActiveOnly, cancellationToken);

        logger.LogInformation("Campaign mediums retrieved. Count: {MediumCount}.",
            mediums.Count);

        return Result.Success<IReadOnlyList<ReferenceItemResponse>>(
            [.. mediums.Select(medium => medium.ToResponse())]);
    }
}