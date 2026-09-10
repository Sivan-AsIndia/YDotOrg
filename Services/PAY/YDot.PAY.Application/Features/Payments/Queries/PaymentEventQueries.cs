using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Payments.Queries;

/// <summary>The payment event queue - SCR-PAY-003.</summary>
public sealed record SearchPaymentEventsQuery(PaymentEventSearchFilter Filter);

/// <summary>One queued event in full, with its raw payload.</summary>
public sealed record GetPaymentEventQuery(Guid PaymentEventId);

/// <summary>Marks an event as needing no action.</summary>
public sealed record DismissPaymentEventCommand(Guid PaymentEventId, DismissPaymentEventRequest Request);

/// <summary>The read side of the payment event queue, plus the one write it owns.</summary>
public sealed class PaymentEventQueryHandler(
    IPaymentEventReadService readService,
    IPaymentEventRepository paymentEvents,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<PaymentEventQueryHandler> logger)
{
    public async Task<Result<PagedResponse<PaymentEventListItemResponse>>> HandleAsync(
        SearchPaymentEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching payment events.");

        var result = await readService.SearchAsync(query.Filter, cancellationToken);

        logger.LogInformation("Payment event search completed.");

        return Result.Success(result);
    }

    public async Task<Result<PaymentEventDetailResponse>> HandleAsync(
        GetPaymentEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving payment event {PaymentEventId}.", query.PaymentEventId);

        var paymentEvent = await readService.GetDetailAsync(query.PaymentEventId, cancellationToken);

        if (paymentEvent is null)
        {
            logger.LogWarning("Payment event {PaymentEventId} was not found.", query.PaymentEventId);

            return Result.Failure<PaymentEventDetailResponse>(
                Error.NotFound("That event was not found."));
        }

        logger.LogInformation("Payment event {PaymentEventId} retrieved successfully.",
            query.PaymentEventId);

        return Result.Success(paymentEvent);
    }

    /// <summary>
    /// Dismisses an event.
    ///
    /// THE EVENT IS KEPT, NOT DELETED. Dismissing says "a person looked at this and decided
    /// nothing was needed", which is a different and more useful statement than the row simply
    /// not existing - and it records WHO decided.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DismissPaymentEventCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Starting dismissal of payment event {PaymentEventId}.",
            command.PaymentEventId);

        var paymentEvent = await paymentEvents.GetAsync(command.PaymentEventId, cancellationToken);

        if (paymentEvent is null)
        {
            logger.LogWarning("Payment event {PaymentEventId} could not be dismissed because it was not found.",
                command.PaymentEventId);

            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That event was not found."));
        }

        if (paymentEvent.Version != command.Request.ExpectedVersion)
        {
            logger.LogWarning("Payment event {PaymentEventId} dismissal rejected because the record version is stale.",
                command.PaymentEventId);

            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (paymentEvent.Status == PaymentEventStatus.Processed)
        {
            logger.LogWarning("Payment event {PaymentEventId} cannot be dismissed because it has already been processed.",
                command.PaymentEventId);

            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That event has already been applied and cannot be dismissed."));
        }

        paymentEvent.Status = PaymentEventStatus.Dismissed;
        paymentEvent.DismissedByUserId = currentUser.UserId;
        paymentEvent.DismissalReason = command.Request.Reason.Trim();
        paymentEvent.ProcessedAtUtc = clock.UtcNow;

        await audit.WriteAsync(
            AuditActionCodes.PaymentEventDismissed,
            nameof(PaymentEvent),
            paymentEvent.Id,
            new { paymentEvent.GatewayEventId, EventType = paymentEvent.EventType.ToString() },
            command.Request.Reason,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Payment event {PaymentEventId} dismissed successfully.",
            paymentEvent.Id);

        return Result.Success(new OutcomeResponse(
            paymentEvent.Id,
            paymentEvent.Status.ToString(),
            paymentEvent.Version,
            "Event dismissed.",
            []));
    }
}