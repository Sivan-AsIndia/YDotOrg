using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Features.Refunds.Queries;

/// <summary>The refund register - SCR-PAY-006.</summary>
public sealed record SearchRefundsQuery(RefundSearchFilter Filter);

/// <summary>One refund case in full.</summary>
public sealed record GetRefundQuery(Guid RefundCaseId);

/// <summary>CSV export of the refund register.</summary>
public sealed record ExportRefundsQuery(RefundSearchFilter Filter);

/// <summary>The chargeback register - SCR-PAY-008.</summary>
public sealed record SearchChargebacksQuery(ChargebackSearchFilter Filter);

/// <summary>One chargeback case in full.</summary>
public sealed record GetChargebackQuery(Guid ChargebackCaseId);

/// <summary>The read side of the Refunds and Chargebacks slice.</summary>
public sealed class RefundQueryHandler(
    IRefundReadService readService,
    ICsvExportService exports,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<RefundQueryHandler> logger)
{
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    private bool CanSeeSensitiveDonor =>
        currentUser.HasPermission(PermissionCodes.DonationsViewSensitiveDonor);

    public async Task<Result<PagedResponse<RefundCaseListItemResponse>>> HandleAsync(
        SearchRefundsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching refund cases.");

        var result = await readService.SearchRefundsAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        logger.LogInformation("Refund case search completed successfully.");

        return Result.Success(result);
    }

    public async Task<Result<RefundCaseDetailResponse>> HandleAsync(
        GetRefundQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving refund case {RefundCaseId}.", query.RefundCaseId);

        var refundCase = await readService.GetRefundDetailAsync(
            query.RefundCaseId, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        if (refundCase is null)
        {
            logger.LogWarning("Refund case {RefundCaseId} was not found.", query.RefundCaseId);
            return Result.Failure<RefundCaseDetailResponse>(Error.NotFound("That refund was not found."));
        }

        logger.LogInformation("Refund case {RefundCaseId} retrieved successfully.", query.RefundCaseId);

        return Result.Success(refundCase);
    }

    public async Task<Result<ExportFile>> HandleAsync(
        ExportRefundsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = CanSeeSensitiveDonor;

        logger.LogInformation("Starting refund case export.");

        var filter = query.Filter;
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        var rows = new List<RefundExportRow>();

        while (filter.Page <= MaximumExportPages)
        {
            var page = await readService.GetRefundExportRowsAsync(
                filter, currentUser.Scope, canSeeSensitive, cancellationToken);

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

        if (filter.Page > MaximumExportPages)
        {
            logger.LogWarning("Refund export reached the maximum export page limit of {MaximumExportPages}.",
                MaximumExportPages);
        }

        var file = exports.ToCsv(rows, "refunds");

        await audit.WriteAsync(
            AuditActionCodes.RefundExported,
            nameof(RefundCase),
            Guid.Empty,
            new { RowCount = rows.Count, file.Reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refund case export completed successfully with {RowCount} rows.",
            rows.Count);

        return Result.Success(file);
    }

    public async Task<Result<PagedResponse<ChargebackCaseListItemResponse>>> HandleAsync(
        SearchChargebacksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching chargeback cases.");

        var result = await readService.SearchChargebacksAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        logger.LogInformation("Chargeback case search completed successfully.");

        return Result.Success(result);
    }

    public async Task<Result<ChargebackCaseDetailResponse>> HandleAsync(
        GetChargebackQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving chargeback case {ChargebackCaseId}.", query.ChargebackCaseId);

        var chargeback = await readService.GetChargebackDetailAsync(
            query.ChargebackCaseId, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        if (chargeback is null)
        {
            logger.LogWarning("Chargeback case {ChargebackCaseId} was not found.", query.ChargebackCaseId);
            return Result.Failure<ChargebackCaseDetailResponse>(
                Error.NotFound("That chargeback was not found."));
        }

        logger.LogInformation("Chargeback case {ChargebackCaseId} retrieved successfully.",
            query.ChargebackCaseId);

        return Result.Success(chargeback);
    }
}