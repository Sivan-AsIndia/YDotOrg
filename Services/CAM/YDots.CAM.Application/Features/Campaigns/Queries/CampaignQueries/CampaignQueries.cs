using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Features.Campaigns.Queries.CampaignQueries;

/// <summary>The campaign register.</summary>
public sealed record SearchCampaignsQuery(CampaignSearchFilter Filter);

/// <summary>One campaign in full.</summary>
public sealed record GetCampaignQuery(Guid CampaignId);

/// <summary>The audit trail for one campaign.</summary>
public sealed record GetCampaignHistoryQuery(Guid CampaignId, PaginationRequest Pagination);

/// <summary>Counts by status, for the register's summary tiles.</summary>
public sealed record GetCampaignStatisticsQuery;

/// <summary>Active campaigns for a picker.</summary>
public sealed record LookupCampaignsQuery(string? Search = null, int Take = 50);

/// <summary>CSV export of the register.</summary>
public sealed record ExportCampaignsQuery(CampaignSearchFilter Filter);

/// <summary>
/// The read side of the Campaigns slice.
///
/// Everything here is a thin pass-through to <see cref="ICampaignReadService"/>, which does the
/// projection. The one piece of real logic is the export loop and the audit row that goes with
/// it - a CSV of every campaign, its targets and its budgets is a copy of commercially
/// sensitive data leaving the system, and it is the event a later review actually looks for.
///
/// READS ARE NOT AUDITED, EXPORTS ARE. Auditing every grid load would bury the exports in noise
/// and make the trail useless for the question it exists to answer.
/// </summary>
public sealed class CampaignQueryHandler(
    ICampaignReadService readService,
    ICsvExportService exports,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// The most pages an export walks. At 100 rows a page that is 50,000 campaigns - far more
    /// than any real Organisation runs, and a hard stop against an export that would otherwise
    /// run until it timed out.
    /// </summary>
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    public async Task<Result<PagedResponse<CampaignListItemResponse>>> HandleAsync(
        SearchCampaignsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(
            await readService.SearchAsync(query.Filter, currentUser.Scope, cancellationToken));
    }

    public async Task<Result<CampaignDetailResponse>> HandleAsync(
        GetCampaignQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var campaign = await readService.GetDetailAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        return campaign is null
            ? Result.Failure<CampaignDetailResponse>(Error.NotFound("That campaign was not found."))
            : Result.Success(campaign);
    }

    public async Task<Result<PagedResponse<CampaignHistoryResponse>>> HandleAsync(
        GetCampaignHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The campaign is resolved first, so a history request for another Organisation's
        // campaign answers 404 rather than an empty page. An empty page would be ambiguous:
        // it reads as "this campaign has no history" rather than "no such campaign".
        var campaign = await readService.GetDetailAsync(
            query.CampaignId, currentUser.Scope, cancellationToken);

        if (campaign is null)
        {
            return Result.Failure<PagedResponse<CampaignHistoryResponse>>(
                Error.NotFound("That campaign was not found."));
        }

        return Result.Success(
            await readService.GetHistoryAsync(query.CampaignId, query.Pagination, cancellationToken));
    }

    public async Task<Result<CampaignStatisticsResponse>> HandleAsync(
        GetCampaignStatisticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.GetStatisticsAsync(currentUser.Scope, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<LookupItem>>> HandleAsync(
        LookupCampaignsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.LookupAsync(query.Search, query.Take, cancellationToken));
    }

    /// <summary>
    /// Exports the register.
    ///
    /// PAGED RATHER THAN ONE UNBOUNDED READ, so a large Organisation does not pull its whole
    /// campaign history into memory as a single query. The filter is MUTATED as the loop walks
    /// it, which is safe because it is a per-request binding model nothing else holds a
    /// reference to.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportCampaignsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = query.Filter;
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        var rows = new List<CampaignExportRow>();

        while (filter.Page <= MaximumExportPages)
        {
            var page = await readService.GetExportRowsAsync(filter, currentUser.Scope, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            rows.AddRange(page);

            // A short page is the last page, so stop rather than issuing one more query that is
            // guaranteed to come back empty.
            if (page.Count < ExportPageSize)
            {
                break;
            }

            filter.Page++;
        }

        var file = exports.ToCsv(rows, "campaigns");

        await audit.WriteAsync(
            AuditActionCodes.CampaignExported, nameof(Campaign), Guid.Empty,
            $"Exported {rows.Count} campaign(s) as {file.Reference}.", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }
}
