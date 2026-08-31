using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Features.Receipts.Queries;

/// <summary>The receipt register - SCR-PAY-005.</summary>
public sealed record SearchReceiptsQuery(ReceiptSearchFilter Filter);

/// <summary>One receipt in full.</summary>
public sealed record GetReceiptQuery(Guid ReceiptId);

/// <summary>CSV export of the register.</summary>
public sealed record ExportReceiptsQuery(ReceiptSearchFilter Filter);

/// <summary>The read side of the Receipts slice.</summary>
public sealed class ReceiptQueryHandler(
    IReceiptReadService readService,
    ICsvExportService exports,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    private bool CanSeeSensitiveDonor =>
        currentUser.HasPermission(PermissionCodes.DonationsViewSensitiveDonor);

    public async Task<Result<PagedResponse<ReceiptListItemResponse>>> HandleAsync(
        SearchReceiptsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken));
    }

    public async Task<Result<ReceiptDetailResponse>> HandleAsync(
        GetReceiptQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var receipt = await readService.GetDetailAsync(
            query.ReceiptId, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        return receipt is null
            ? Result.Failure<ReceiptDetailResponse>(Error.NotFound("That receipt was not found."))
            : Result.Success(receipt);
    }

    public async Task<Result<ExportFile>> HandleAsync(
        ExportReceiptsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeSensitive = CanSeeSensitiveDonor;

        var filter = query.Filter;
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        var rows = new List<ReceiptExportRow>();

        while (filter.Page <= MaximumExportPages)
        {
            var page = await readService.GetExportRowsAsync(
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

        var file = exports.ToCsv(rows, "receipts");

        await audit.WriteAsync(
            AuditActionCodes.ReceiptExported,
            nameof(Receipt),
            Guid.Empty,
            new { RowCount = rows.Count, file.Reference, UnmaskedDonorDetails = canSeeSensitive },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }
}
