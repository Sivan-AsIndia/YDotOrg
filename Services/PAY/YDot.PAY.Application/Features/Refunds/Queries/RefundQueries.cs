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
    IUnitOfWork unitOfWork)
{
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    private bool CanSeeSensitiveDonor =>
        currentUser.HasPermission(PermissionCodes.DonationsViewSensitiveDonor);

    public async Task<Result<PagedResponse<RefundCaseListItemResponse>>> HandleAsync(
        SearchRefundsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchRefundsAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken));
    }

    public async Task<Result<RefundCaseDetailResponse>> HandleAsync(
        GetRefundQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var refundCase = await readService.GetRefundDetailAsync(
            query.RefundCaseId, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        return refundCase is null
            ? Result.Failure<RefundCaseDetailResponse>(Error.NotFound("That refund was not found."))
            : Result.Success(refundCase);
    }

    public async Task<Result<ExportFile>> HandleAsync(
        ExportRefundsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = CanSeeSensitiveDonor;

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

        var file = exports.ToCsv(rows, "refunds");

        await audit.WriteAsync(
            AuditActionCodes.RefundExported,
            nameof(RefundCase),
            Guid.Empty,
            new { RowCount = rows.Count, file.Reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }

    public async Task<Result<PagedResponse<ChargebackCaseListItemResponse>>> HandleAsync(
        SearchChargebacksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchChargebacksAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken));
    }

    public async Task<Result<ChargebackCaseDetailResponse>> HandleAsync(
        GetChargebackQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var chargeback = await readService.GetChargebackDetailAsync(
            query.ChargebackCaseId, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        return chargeback is null
            ? Result.Failure<ChargebackCaseDetailResponse>(
                Error.NotFound("That chargeback was not found."))
            : Result.Success(chargeback);
    }
}
