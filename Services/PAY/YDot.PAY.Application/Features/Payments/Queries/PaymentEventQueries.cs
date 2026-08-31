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
    IUnitOfWork unitOfWork)
{
    public async Task<Result<PagedResponse<PaymentEventListItemResponse>>> HandleAsync(
        SearchPaymentEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchAsync(query.Filter, cancellationToken));
    }

    public async Task<Result<PaymentEventDetailResponse>> HandleAsync(
        GetPaymentEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var paymentEvent = await readService.GetDetailAsync(query.PaymentEventId, cancellationToken);

        return paymentEvent is null
            ? Result.Failure<PaymentEventDetailResponse>(Error.NotFound("That event was not found."))
            : Result.Success(paymentEvent);
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

        var paymentEvent = await paymentEvents.GetAsync(command.PaymentEventId, cancellationToken);

        if (paymentEvent is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That event was not found."));
        }

        if (paymentEvent.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (paymentEvent.Status == PaymentEventStatus.Processed)
        {
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

        return Result.Success(new OutcomeResponse(
            paymentEvent.Id,
            paymentEvent.Status.ToString(),
            paymentEvent.Version,
            "Event dismissed.",
            []));
    }
}
