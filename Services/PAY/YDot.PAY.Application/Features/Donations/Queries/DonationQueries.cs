using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Features.Donations.Queries;

/// <summary>The donation intent register.</summary>
public sealed record SearchDonationIntentsQuery(DonationIntentSearchFilter Filter);

/// <summary>One intent in full - SCR-PAY-001.</summary>
public sealed record GetDonationIntentQuery(Guid IntentId);

/// <summary>An intent by its public reference. Used by the donor-facing result page.</summary>
public sealed record GetDonationIntentByReferenceQuery(string IntentReference);

/// <summary>The support queue: intents that failed and need a person. Section 23.</summary>
public sealed record GetPaymentSupportQueueQuery(PaginationRequest Pagination);

/// <summary>The donation register.</summary>
public sealed record SearchDonationsQuery(DonationSearchFilter Filter);

/// <summary>One donation in full.</summary>
public sealed record GetDonationQuery(Guid DonationId);

/// <summary>Counts and totals for the register tiles.</summary>
public sealed record GetDonationStatisticsQuery;

/// <summary>CSV export of the donation register.</summary>
public sealed record ExportDonationsQuery(DonationSearchFilter Filter);

/// <summary>
/// The read side of the Donations slice.
///
/// EVERY METHOD PASSES <c>canSeeSensitiveDonor</c> DOWN TO THE PROJECTION rather than masking
/// afterwards. A read service that returned real addresses and trusted each caller to mask them
/// would leak the first time one endpoint forgot - and these projections feed six different
/// screens plus two exports.
///
/// VIEWING UNMASKED DONOR DETAILS IS AUDITED. Holding the permission is not the same as using
/// it, and "who looked at this donor's tax identifier?" is a question a data-protection review
/// will ask.
/// </summary>
public sealed class DonationQueryHandler(
    IDonationIntentReadService intentReadService,
    IDonationReadService donationReadService,
    ICsvExportService exports,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<DonationQueryHandler> logger)
{
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    private bool CanSeeSensitiveDonor =>
        currentUser.HasPermission(PermissionCodes.DonationsViewSensitiveDonor);

    // ---- Intents ---------------------------------------------------------------------

    public async Task<Result<PagedResponse<DonationIntentListItemResponse>>> HandleAsync(
        SearchDonationIntentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching donation intents.");

        var result = await intentReadService.SearchAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        logger.LogInformation("Donation intent search completed.");

        return Result.Success(result);
    }

    public async Task<Result<DonationIntentDetailResponse>> HandleAsync(
        GetDonationIntentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving donation intent {IntentId}.", query.IntentId);

        var canSeeSensitive = CanSeeSensitiveDonor;

        var intent = await intentReadService.GetDetailAsync(
            query.IntentId, currentUser.Scope, canSeeSensitive, cancellationToken);

        if (intent is null)
        {
            logger.LogWarning("Donation intent {IntentId} was not found.", query.IntentId);

            return Result.Failure<DonationIntentDetailResponse>(
                Error.NotFound("That donation was not found."));
        }

        if (canSeeSensitive)
        {
            logger.LogInformation(
                "Sensitive donor details accessed for donation intent {IntentId}.",
                query.IntentId);

            await audit.WriteAsync(
                AuditActionCodes.DonationSensitiveDonorViewed,
                nameof(DonationIntent),
                query.IntentId,
                new { intent.IntentReference },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Donation intent {IntentId} retrieved successfully.", query.IntentId);

        return Result.Success(intent);
    }

    /// <summary>
    /// An intent by its public reference.
    ///
    /// USED BY THE DONOR-FACING RESULT PAGE, which has no session - so the details always come
    /// back masked. The donor sees their own donation through the reference they hold; nothing
    /// here reveals more than the payment link already did.
    /// </summary>
    public async Task<Result<DonationIntentDetailResponse>> HandleAsync(
        GetDonationIntentByReferenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving donation intent by public reference.");

        var intent = await intentReadService.GetDetailByReferenceAsync(
            query.IntentReference, cancellationToken);

        if (intent is null)
        {
            logger.LogWarning("Donation intent was not found for the supplied public reference.");

            return Result.Failure<DonationIntentDetailResponse>(
                Error.NotFound("That donation was not found."));
        }

        logger.LogInformation("Donation intent retrieved successfully by public reference.");

        return Result.Success(intent);
    }

    public async Task<Result<PagedResponse<PaymentSupportCaseResponse>>> HandleAsync(
        GetPaymentSupportQueueQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving payment support queue.");

        var result = await intentReadService.GetSupportQueueAsync(
            query.Pagination, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        logger.LogInformation("Payment support queue retrieved successfully.");

        return Result.Success(result);
    }

    // ---- Donations ----------------------------------------------------------------------------

    public async Task<Result<PagedResponse<DonationListItemResponse>>> HandleAsync(
        SearchDonationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching donations.");

        var result = await donationReadService.SearchAsync(
            query.Filter, currentUser.Scope, CanSeeSensitiveDonor, cancellationToken);

        logger.LogInformation("Donation search completed.");

        return Result.Success(result);
    }

    public async Task<Result<DonationDetailResponse>> HandleAsync(
        GetDonationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving donation {DonationId}.", query.DonationId);

        var canSeeSensitive = CanSeeSensitiveDonor;

        var donation = await donationReadService.GetDetailAsync(
            query.DonationId, currentUser.Scope, canSeeSensitive, cancellationToken);

        if (donation is null)
        {
            logger.LogWarning("Donation {DonationId} was not found.", query.DonationId);

            return Result.Failure<DonationDetailResponse>(
                Error.NotFound("That donation was not found."));
        }

        if (canSeeSensitive)
        {
            logger.LogInformation(
                "Sensitive donor details accessed for donation {DonationId}.",
                query.DonationId);

            await audit.WriteAsync(
                AuditActionCodes.DonationSensitiveDonorViewed,
                nameof(Donation),
                query.DonationId,
                new { donation.DonationReference },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Donation {DonationId} retrieved successfully.", query.DonationId);

        return Result.Success(donation);
    }

    public async Task<Result<DonationStatisticsResponse>> HandleAsync(
        GetDonationStatisticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving donation statistics.");

        var statistics = await donationReadService.GetStatisticsAsync(
            currentUser.Scope, cancellationToken);

        logger.LogInformation("Donation statistics retrieved successfully.");

        return Result.Success(statistics);
    }

    /// <summary>
    /// Exports the donation register.
    ///
    /// AUDITED, and this is one of the more consequential exports on the platform: a CSV of every
    /// donation, who gave it and how much, which outlives the session and travels by e-mail.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportDonationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting donation register export.");

        var canSeeSensitive = CanSeeSensitiveDonor;

        var filter = query.Filter;
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        var rows = new List<DonationExportRow>();

        while (filter.Page <= MaximumExportPages)
        {
            var page = await donationReadService.GetExportRowsAsync(
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

        var file = exports.ToCsv(rows, "donations");

        await audit.WriteAsync(
            AuditActionCodes.DonationExported,
            nameof(Donation),
            Guid.Empty,
            new { RowCount = rows.Count, file.Reference, UnmaskedDonorDetails = canSeeSensitive },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Donation register export completed successfully with {RowCount} rows.",
            rows.Count);

        return Result.Success(file);
    }
}