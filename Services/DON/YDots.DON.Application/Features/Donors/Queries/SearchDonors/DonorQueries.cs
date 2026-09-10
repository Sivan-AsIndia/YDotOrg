using Microsoft.Extensions.Logging;
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
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.Donors.Queries.SearchDonors;

/// <summary>GET /api/v1/donors. View permission plus data scope.</summary>
public sealed record SearchDonorsQuery(DonorSearchFilter Filter);

/// <summary>GET /api/v1/donors/{id}. View permission plus record scope.</summary>
public sealed record GetDonorDetailQuery(Guid DonorId);

/// <summary>GET /api/v1/donors/lookup. Fills the donor autocomplete on the other screens.</summary>
public sealed record LookupDonorsQuery(string? Search, int MaximumRows);

/// <summary>GET /api/v1/donors/export. Controlled CSV of the rows the caller can already see.</summary>
public sealed record ExportDonorsQuery(DonorSearchFilter Filter);

/// <summary>
/// The read side of the Donor resource. Every method hands <see cref="ICurrentUser.Scope"/>
/// to the read service, so the scope restriction travels with the query rather than being
/// something a repository has to remember.
/// </summary>
public sealed class DonorQueryHandler(
    IDonorReadService readService,
    IExportService exportService,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IOptions<DonorSettings> donorSettings,
    ILogger<DonorQueryHandler> logger)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<PagedResponse<DonorListItemResponse>>> HandleAsync(
        SearchDonorsQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Search donors started.");

        var page = await readService.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);

        logger.LogInformation("Search donors completed successfully. Returned {RowCount} row(s).", page.Items.Count);

        return Result.Success(page);
    }

    public async Task<Result<DonorDetailResponse>> HandleAsync(
        GetDonorDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Get donor detail started for DonorId {DonorId}.", query.DonorId);

        var detail = await readService.GetDetailAsync(query.DonorId, currentUser.Scope, cancellationToken);

        if (detail is null)
        {
            logger.LogWarning("Get donor detail failed for DonorId {DonorId} because the donor was not found within the current scope.", query.DonorId);
            return Result.Failure<DonorDetailResponse>(Error.DonorNotFound());
        }

        // Section 10: a sensitive view is an audited event in its own right, so opening a
        // record with the unmasking permission leaves a trace.
        if (currentUser.CanSeeContact())
        {
            logger.LogInformation("Sensitive donor view detected for DonorId {DonorId}.", query.DonorId);

            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.DonorSensitiveViewed, nameof(Donor), query.DonorId,
                    AuditResult.Succeeded, $"{detail.DonorNumber} viewed with unmasked contact details."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Sensitive donor view audit recorded successfully for DonorId {DonorId}.", query.DonorId);
        }

        logger.LogInformation("Get donor detail completed successfully for DonorId {DonorId}.", query.DonorId);

        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<DonorLookupResponse>>> HandleAsync(
        LookupDonorsQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Donor lookup started.");

        var rows = query.MaximumRows is <= 0 or > 50 ? 20 : query.MaximumRows;
        var items = await readService.LookupAsync(query.Search, rows, currentUser.Scope, cancellationToken);

        logger.LogInformation("Donor lookup completed successfully. Returned {RowCount} row(s).", items.Count);

        return Result.Success(items);
    }

    public async Task<Result<ExportFile>> HandleAsync(
        ExportDonorsQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Export donors started with maximum row limit {MaximumRows}.", _settings.ExportMaximumRows);

        var items = await readService.ExportRowsAsync(
            query.Filter, _settings.ExportMaximumRows, currentUser.Scope, cancellationToken);

        var rows = items
            .Select(item => (IReadOnlyList<string>)
            [
                item.DisplayCode,
                item.DisplayName,
                item.Status,
                item.UpdatedAtUtc.ToString("u"),
                item.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ])
            .ToList();

        var file = exportService.CreateCsv(
            "ydot-donors",
            ["Donor number", "Display name", "Status", "Last updated", "Version"],
            rows);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.DonorExported, nameof(Donor), null, AuditResult.Succeeded,
                $"{rows.Count} row(s) exported. Reference {file.Reference}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Export donors completed successfully. Exported {RowCount} row(s).", rows.Count);

        return Result.Success(file);
    }
}