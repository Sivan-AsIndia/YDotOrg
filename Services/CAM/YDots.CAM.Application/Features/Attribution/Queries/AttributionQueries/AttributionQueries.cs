using Microsoft.Extensions.Logging;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Attribution.DTOs;

namespace YDots.CAM.Application.Features.Attribution.Queries.AttributionQueries;

/// <summary>One page of attributed donations.</summary>
public sealed record SearchAttributionQuery(AttributionSearchFilter Filter);

/// <summary>One donation's full attribution trail.</summary>
public sealed record GetAttributionQuery(Guid DonationId);

/// <summary>How income breaks down by channel, source, medium and asset.</summary>
public sealed record GetAttributionSummaryQuery(Guid? CampaignId);

/// <summary>The explorer as a CSV.</summary>
public sealed record ExportAttributionQuery(AttributionSearchFilter Filter);

/// <summary>The read side of the Attribution slice.</summary>
public sealed class AttributionQueryHandler(
    IAttributionReadService readService,
    ICsvExportService csv,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<AttributionQueryHandler> logger)
{
    public async Task<Result<PagedResponse<AttributionListItemResponse>>> HandleAsync(
        SearchAttributionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching attribution records. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page, query.Filter.PageSize);

        var page = await readService.SearchAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation("Attribution search completed. TotalCount: {TotalCount}.",
            page.TotalCount);

        return Result.Success(page);
    }

    public async Task<Result<AttributionDetailResponse>> HandleAsync(
        GetAttributionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving attribution detail. DonationId: {DonationId}.",
            query.DonationId);

        var detail = await readService.GetAsync(
            query.DonationId, currentUser.Scope, cancellationToken);

        if (detail is null)
        {
            logger.LogWarning("Attribution detail not found. DonationId: {DonationId}.",
                query.DonationId);

            return Result.Failure<AttributionDetailResponse>(
                Error.NotFound("That donation was not found inside your scope."));
        }

        logger.LogInformation("Attribution detail retrieved. DonationId: {DonationId}.",
            query.DonationId);

        return Result.Success(detail);
    }

    public async Task<Result<AttributionSummaryResponse>> HandleAsync(
        GetAttributionSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving attribution summary. CampaignId: {CampaignId}.",
            query.CampaignId);

        var summary = await readService.GetSummaryAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        logger.LogInformation("Attribution summary retrieved. CampaignId: {CampaignId}.",
            query.CampaignId);

        return Result.Success(summary);
    }

    /// <summary>
    /// The explorer as a CSV.
    ///
    /// AUDITED. This export carries donor names alongside amounts, which makes it the one
    /// attribution action that produces a file of personal data outliving the session.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportAttributionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting attribution export.");

        var rows = await readService.ListForExportAsync(
            query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation("Attribution export rows retrieved. RowCount: {RowCount}.",
            rows.Count);

        var file = csv.ToCsv(rows.Select(row => new
        {
            Donation = row.Reference,
            Received = row.ReceivedAtUtc,
            Amount = row.Amount,
            Currency = row.CurrencyCode,
            Status = row.Status,
            Campaign = row.CampaignCode,
            CampaignName = row.CampaignName,
            TrackingReference = row.TrackingReference,
            Channel = row.ChannelName,
            Source = row.SourceName,
            Medium = row.MediumName,
            Donor = row.DonorName,
            Attributed = row.IsAttributed,
            OpenCorrection = row.HasOpenCorrectionRequest
        }).ToList(), "attributed-donations");

        await audit.WriteAsync(
            AttributionAuditActionCodes.Exported, nameof(AttributionListItemResponse), Guid.Empty,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Attribution export completed. RowCount: {RowCount}, Reference: {Reference}.",
            rows.Count, file.Reference);

        return Result.Success(file);
    }
}