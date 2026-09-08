using Microsoft.Extensions.Logging;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Features.TrackingAssets.Queries.TrackingAssetQueries;

/// <summary>The tracking asset manager grid.</summary>
public sealed record SearchTrackingAssetsQuery(TrackingAssetSearchFilter Filter);

/// <summary>One tracking asset in full.</summary>
public sealed record GetTrackingAssetQuery(Guid TrackingAssetId);

/// <summary>CSV export of the manager.</summary>
public sealed record ExportTrackingAssetsQuery(TrackingAssetSearchFilter Filter);

/// <summary>The read side of the Tracking Assets slice.</summary>
public sealed class TrackingAssetQueryHandler(
    ITrackingAssetReadService readService,
    ICsvExportService exports,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<TrackingAssetQueryHandler> logger)
{
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    public async Task<Result<PagedResponse<TrackingAssetListItemResponse>>> HandleAsync(
        SearchTrackingAssetsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching tracking assets. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page, query.Filter.PageSize);

        var result = await readService.SearchAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation(
            "Tracking asset search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<TrackingAssetDetailResponse>> HandleAsync(
        GetTrackingAssetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving tracking asset detail. TrackingAssetId: {TrackingAssetId}.",
            query.TrackingAssetId);

        var asset = await readService.GetDetailAsync(
            query.TrackingAssetId, currentUser.Scope, cancellationToken);

        if (asset is null)
        {
            logger.LogWarning(
                "Tracking asset not found. TrackingAssetId: {TrackingAssetId}.",
                query.TrackingAssetId);

            return Result.Failure<TrackingAssetDetailResponse>(
                Error.NotFound("That tracking asset was not found."));
        }

        logger.LogInformation(
            "Tracking asset detail retrieved. TrackingAssetId: {TrackingAssetId}.",
            query.TrackingAssetId);

        return Result.Success(asset);
    }

    /// <summary>
    /// Exports the manager.
    ///
    /// AUDITED, and this one matters more than most: the file contains every live tracking
    /// reference, which is the attribution key a donation carries. Anybody holding the list
    /// knows exactly which URL credits which campaign.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportTrackingAssetsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting tracking asset export.");

        var filter = query.Filter;
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        var rows = new List<TrackingAssetExportRow>();

        while (filter.Page <= MaximumExportPages)
        {
            logger.LogInformation(
                "Retrieving tracking asset export page. Page: {Page}, PageSize: {PageSize}.",
                filter.Page, filter.PageSize);

            var page = await readService.GetExportRowsAsync(
                filter, currentUser.Scope, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            rows.AddRange(page);

            if (page.Count < ExportPageSize)
            {
                break;
            }

            filter.Page++;
        }

        var file = exports.ToCsv(rows, "tracking-assets");

        await audit.WriteAsync(
            TrackingAssetAuditActionCodes.Exported, nameof(TrackingAsset), Guid.Empty,
            $"Exported {rows.Count} tracking asset(s) as {file.Reference}.", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Tracking asset export completed. RowCount: {RowCount}, Reference: {Reference}.",
            rows.Count, file.Reference);

        return Result.Success(file);
    }
}