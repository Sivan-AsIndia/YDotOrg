using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Mappings;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Payments.Commands.ProcessPayment;

/// <summary>A gateway webhook arrived. Store it, then apply it.</summary>
public sealed record IngestGatewayWebhookCommand(
    string GatewayName, string Payload, string? SignatureHeader);

/// <summary>Applies a stored gateway event to the payment it concerns.</summary>
public sealed record ApplyPaymentEventCommand(Guid PaymentEventId);

/// <summary>Asks the gateway what actually happened to an attempt.</summary>
public sealed record VerifyPaymentCommand(VerifyPaymentRequest Request);

/// <summary>
/// What the browser brings back when an in-page checkout finishes, and the signature that says
/// it really came from the provider.
/// </summary>
public sealed record ConfirmCheckoutPaymentCommand(
    string IntentReference, ConfirmCheckoutRequest Request);

/// <summary>Section 23: retry a failed payment, safely.</summary>
public sealed record SafeRetryCommand(Guid IntentId, SafeRetryRequest Request);

/// <summary>
/// Turning money into a donation, and a payer into a donor.
///
/// THIS IS SECTIONS 15 TO 18 OF THE MODULE BRIEF, and the order of operations is the whole
/// thing:
///
/// <code>
/// Payment success
///   -> Record the donation          the money is now real
///   -> Find or create the donor      section 15, from the intent
///   -> Create the account, invite    section 17, no password set here
///   -> Convert the lead              sections 16 and 28
///   -> Issue the receipt             section 24
/// </code>
///
/// THE DONATION IS RECORDED FIRST AND EVERYTHING ELSE IS BEST-EFFORT. A donor account that
/// could not be created, an invitation that would not send, a receipt that failed to render -
/// none of those is a reason to reject money that has already been taken. They are follow-up
/// tasks, and they are logged as such. The alternative is a charity that has the donor's money
/// and no record of the gift because an e-mail server was down.
///
/// APPLYING AN EVENT IS TRANSACTIONAL AND IDEMPOTENT. A gateway may deliver the same capture
/// twice, out of order, or after we already learned the outcome by polling - so the work runs
/// inside a transaction, and the unique index on the gateway event id is what makes a redelivery
/// a no-op rather than a second donation.
/// </summary>
public sealed class PaymentProcessingCommandHandler(
    IDonationRepository donations,
    IPaymentEventRepository paymentEvents,
    IGatewayAccountRepository gatewayAccounts,
    IReceiptRepository receipts,
    IReceiptDocumentService receiptDocuments,
    IDonorDirectory donorDirectory,
    ICampaignDirectory campaignDirectory,
    IPaymentGateway paymentGateway,
    IReferenceGenerator references,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<PaymentSettings> paymentOptions,
    IUnitOfWork unitOfWork,
    ILogger<PaymentProcessingCommandHandler> logger)
{
    private readonly PaymentSettings _settings = paymentOptions.Value;

    private const int ReferenceAttempts = 5;

    // =====================================================================================
    // Webhook ingestion
    // =====================================================================================

    /// <summary>
    /// Receives a gateway webhook.
    ///
    /// IT STORES BEFORE IT INTERPRETS, and that ordering is the design. A webhook that crashes
    /// the processor is still on disk to be replayed; one that arrives twice is recognisably a
    /// duplicate; one whose signature fails is evidence rather than a silent 401.
    ///
    /// AN UNVERIFIED SIGNATURE IS STORED AND NEVER ACTED ON. Anybody can post to a webhook URL,
    /// so the signature is the only thing that says the gateway sent it - and a forgery attempt
    /// is exactly the row somebody should be able to find afterwards.
    /// </summary>
    public async Task<Result<Guid>> HandleAsync(
        IngestGatewayWebhookCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsed = paymentGateway.ParseWebhook(command.Payload);

        if (parsed is null)
        {
            logger.LogWarning(
                "A webhook from {GatewayName} could not be parsed and was ignored.", command.GatewayName);

            return Result.Failure<Guid>(Error.Validation("The webhook payload could not be read."));
        }

        // DUPLICATE DELIVERY IS THE NORM, not the exception - gateways retry for days. Returning
        // the existing event's id makes a redelivery a no-op the provider sees as success.
        var existing = await paymentEvents.FindByGatewayEventIdAsync(
            command.GatewayName, parsed.GatewayEventId, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Gateway event {GatewayEventId} was already received. Ignoring the redelivery.",
                parsed.GatewayEventId);

            return Result.Success(existing.Id);
        }

        // The attempt is resolved first, because it carries the Organisation - a webhook arrives
        // with no session and nothing else to scope it by.
        var attempt = string.IsNullOrWhiteSpace(parsed.GatewayReference)
            ? null
            : await donations.GetAttemptByGatewayReferenceAsync(parsed.GatewayReference, cancellationToken);

        var tenantId = attempt?.TenantId;

        var signatureVerified = false;

        if (tenantId.HasValue)
        {
            var account = await gatewayAccounts.GetActiveForTenantAsync(tenantId.Value, cancellationToken);

            signatureVerified = account is not null
                                && !string.IsNullOrWhiteSpace(command.SignatureHeader)
                                && paymentGateway.VerifyWebhookSignature(
                                    account, command.Payload, command.SignatureHeader);
        }

        var now = clock.UtcNow;

        var paymentEvent = new PaymentEvent
        {
            TenantId = tenantId ?? Guid.Empty,
            BusinessUnitId = attempt?.BusinessUnitId ?? Guid.Empty,
            PaymentAttemptId = attempt?.Id,
            DonationIntentId = attempt?.DonationIntentId,
            EventType = parsed.EventType,
            Status = PaymentEventStatus.Pending,
            GatewayName = command.GatewayName,
            GatewayEventId = parsed.GatewayEventId,
            GatewayReference = parsed.GatewayReference,
            Amount = parsed.Amount.HasValue && !string.IsNullOrWhiteSpace(parsed.CurrencyCode)
                ? MoneyValue.Create(parsed.Amount.Value, parsed.CurrencyCode)
                : null,
            OccurredAtUtc = parsed.OccurredAtUtc,
            ReceivedAtUtc = now,
            RawPayload = command.Payload,
            SignatureVerified = signatureVerified
        };

        await paymentEvents.AddAsync(paymentEvent, cancellationToken);

        await audit.WriteAnonymousAsync(
            signatureVerified
                ? AuditActionCodes.PaymentEventReceived
                : AuditActionCodes.PaymentEventSignatureRejected,
            nameof(PaymentEvent),
            paymentEvent.Id,
            tenantId,
            signatureVerified ? AuditResult.Succeeded : AuditResult.Denied,
            new { paymentEvent.GatewayEventId, EventType = parsed.EventType.ToString() },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!signatureVerified)
        {
            logger.LogWarning(
                "Gateway event {GatewayEventId} failed signature verification and will not be "
                + "processed. This is either a misconfiguration or a forgery attempt.",
                parsed.GatewayEventId);

            return Result.Success(paymentEvent.Id);
        }

        // Applied immediately, but a failure here does not fail the webhook: the event is stored
        // and the queue will retry it. Answering the provider with an error would only make them
        // redeliver something we already have.
        var applied = await HandleAsync(new ApplyPaymentEventCommand(paymentEvent.Id), cancellationToken);

        if (applied.IsFailure)
        {
            logger.LogError(
                "Gateway event {GatewayEventId} was stored but could not be applied: {Message}",
                parsed.GatewayEventId, applied.Error!.Message);
        }

        return Result.Success(paymentEvent.Id);
    }

    // =====================================================================================
    // Applying an event
    // =====================================================================================

    /// <summary>
    /// Applies a stored gateway event.
    ///
    /// RUNS IN A TRANSACTION, because it reads the current state, decides, and writes - and two
    /// capture events for one payment arriving at once would otherwise both read "not yet paid"
    /// and both record a donation.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApplyPaymentEventCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await ApplyEventAsync(command.PaymentEventId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A REFUSED WRITE IS A RESULT, NOT AN EXCEPTION THE CALLER HAS TO SURVIVE.
            //
            // Both callers of this method already treat it as returning a Result and log a
            // failure - the webhook path says so in as many words: "a failure here does not fail
            // the webhook: the event is stored and the queue will retry it". But a constraint
            // violation reached them as a thrown DbUpdateException instead, and neither caught
            // it. On the webhook path that answers the provider with a 500 and invites a
            // redelivery; on the verification path it reached the DONOR'S RESULT PAGE, which
            // printed the exception text - "An error occurred while saving the entity changes.
            // See the inner exception for details." - to somebody who had just given money.
            //
            // THE TRANSACTION HAS ALREADY ROLLED BACK by the time this runs, which is what makes
            // it safe: the event stays Pending and the queue can retry it, exactly as an
            // unapplied event is meant to.
            //
            // THE INNER EXCEPTION IS WHAT GETS LOGGED. `DbUpdateException`'s own message says to
            // look at the inner one and carries nothing else; the provider's message underneath
            // is the part that names the constraint. This layer does not reference EF Core - by
            // design - so the type is not named here and the unwrapping is generic.
            //
            // CANCELLATION IS NOT SWALLOWED. A cancelled request is the caller going away, not a
            // payment that failed, and turning it into a recorded failure would be a lie.
            logger.LogError(
                exception,
                "Payment event {PaymentEventId} could not be applied: {Reason}",
                command.PaymentEventId,
                DescribeFailure(exception));

            return Result.Failure<OutcomeResponse>(Error.Dependency(
                "This payment could not be recorded. It has been kept for reprocessing and needs "
                + "attention - do not take a second payment."));
        }
    }

    /// <summary>The body of <see cref="ApplyPaymentEventCommand"/>, inside its transaction.</summary>
    private async Task<Result<OutcomeResponse>> ApplyEventAsync(
        Guid paymentEventId, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var paymentEvent = await paymentEvents.GetAsync(paymentEventId, token);

            if (paymentEvent is null)
            {
                return Result.Failure<OutcomeResponse>(Error.NotFound("That payment event was not found."));
            }

            if (paymentEvent.Status == PaymentEventStatus.Processed)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.InvalidTransition("That payment event has already been applied."));
            }

            if (!paymentEvent.SignatureVerified)
            {
                return Result.Failure<OutcomeResponse>(Error.Forbidden(
                    "This event's signature could not be verified, so it will not be applied."));
            }

            paymentEvent.ProcessingAttempts += 1;

            var attempt = paymentEvent.PaymentAttemptId.HasValue
                ? await donations.GetAttemptAsync(paymentEvent.PaymentAttemptId.Value, token)
                : null;

            if (attempt is null)
            {
                // An event we cannot match to an attempt is not a failure of ours - it may be for
                // a payment created outside this system entirely. It sits in the queue for a
                // person rather than being retried forever.
                paymentEvent.Status = PaymentEventStatus.Failed;
                paymentEvent.ProcessingError =
                    "No payment attempt matches this event's gateway reference.";

                await unitOfWork.SaveChangesAsync(token);

                return Result.Failure<OutcomeResponse>(Error.NotFound(
                    "No payment attempt matches this event."));
            }

            var intent = await donations.GetIntentAsync(attempt.DonationIntentId, token);

            if (intent is null)
            {
                paymentEvent.Status = PaymentEventStatus.Failed;
                paymentEvent.ProcessingError = "The attempt has no donation intent.";

                await unitOfWork.SaveChangesAsync(token);

                return Result.Failure<OutcomeResponse>(Error.Dependency(
                    "That payment attempt is not linked to a donation."));
            }

            var outcome = paymentEvent.EventType switch
            {
                PaymentEventType.Captured => await ApplyCaptureAsync(paymentEvent, attempt, intent, token),

                PaymentEventType.Authorised => ApplyAuthorised(attempt),

                PaymentEventType.Failed => ApplyFailure(attempt, intent, paymentEvent),

                PaymentEventType.Cancelled => ApplyCancelled(attempt, intent),

                PaymentEventType.Expired => ApplyExpired(attempt, intent),

                PaymentEventType.Settled => await ApplySettlementAsync(intent, paymentEvent, token),

                // Refunds and chargebacks arriving by webhook are recorded against the donation
                // by their own handlers; here the event is simply marked seen so the queue does
                // not hold it open.
                _ => Result.Success("No action was required for this event type.")
            };

            paymentEvent.ProcessedAtUtc = clock.UtcNow;

            if (outcome.IsFailure)
            {
                paymentEvent.Status = PaymentEventStatus.Failed;
                paymentEvent.ProcessingError = outcome.Error!.Message;
            }
            else
            {
                paymentEvent.Status = PaymentEventStatus.Processed;
                paymentEvent.ProcessingError = null;
            }

            await audit.WriteAnonymousAsync(
                outcome.IsSuccess
                    ? AuditActionCodes.PaymentEventProcessed
                    : AuditActionCodes.PaymentEventFailed,
                nameof(PaymentEvent),
                paymentEvent.Id,
                paymentEvent.TenantId,
                outcome.IsSuccess ? AuditResult.Succeeded : AuditResult.Failed,
                new { paymentEvent.GatewayEventId, EventType = paymentEvent.EventType.ToString() },
                token);

            await unitOfWork.SaveChangesAsync(token);

            return outcome.IsFailure
                ? Result.Failure<OutcomeResponse>(outcome.Error!)
                : Result.Success(new OutcomeResponse(
                    paymentEvent.Id,
                    paymentEvent.Status.ToString(),
                    paymentEvent.Version,
                    outcome.Value!,
                    []));
        }, cancellationToken);
    }

    /// <summary>
    /// The reason a write was refused, from the exception that actually knows it.
    ///
    /// <c>DbUpdateException</c> carries one fixed sentence - "An error occurred while saving the
    /// entity changes. See the inner exception for details." - and every diagnosable fact is on
    /// the provider exception underneath it: the constraint name, the column, the value. Logging
    /// the outer message alone is why a refused donation write left nothing in the log a person
    /// could act on, and why that same useless sentence is what the donor ended up reading.
    /// </summary>
    private static string DescribeFailure(Exception exception)
    {
        var inner = exception.InnerException;

        var described = inner is null
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{exception.GetType().Name}: {inner.GetType().Name}: {inner.Message}";

        // A CONCURRENCY FAILURE IS USELESS WITHOUT THE ENTITY THAT LOST. Its message names a row
        // count and nothing else - "expected to affect 1 row(s), but actually affected 0" - so
        // without this a person reading the log knows a write was refused and has no way to find
        // out which one. `Entries` is on DbUpdateConcurrencyException, which this layer cannot
        // name (no EF reference, by design), so it is read reflectively and skipped if absent.
        var entries = exception.GetType()
            .GetProperty("Entries")?
            .GetValue(exception) as System.Collections.IEnumerable;

        if (entries is null)
        {
            return described;
        }

        var names = new List<string>();

        foreach (var entry in entries)
        {
            var metadata = entry?.GetType().GetProperty("Metadata")?.GetValue(entry);
            var name = metadata?.GetType().GetProperty("Name")?.GetValue(metadata) as string;
            var state = entry?.GetType().GetProperty("State")?.GetValue(entry);

            names.Add($"{name ?? entry?.GetType().Name} ({state})");
        }

        return names.Count == 0 ? described : $"{described} Conflicting entries: {string.Join(", ", names)}.";
    }

    /// <summary>
    /// Money captured: sections 15 to 18 in one method.
    ///
    /// THE DONATION IS RECORDED FIRST. Everything after it - the donor, the account, the
    /// invitation, the lead conversion, the receipt - is best-effort, because none of them is a
    /// reason to lose a gift that has already been taken.
    /// </summary>
    private async Task<Result<string>> ApplyCaptureAsync(
        PaymentEvent paymentEvent,
        PaymentAttempt attempt,
        DonationIntent intent,
        CancellationToken cancellationToken)
    {
        // ALREADY RECORDED? Then this is a redelivery and there is nothing to do. This is the
        // check that stops one payment becoming two donations.
        var existing = await donations.GetDonationByIntentAsync(intent.Id, cancellationToken);

        if (existing is not null)
        {
            paymentEvent.Status = PaymentEventStatus.Duplicate;

            logger.LogInformation(
                "Capture event for intent {IntentReference} ignored: donation {DonationReference} "
                + "already exists.", intent.IntentReference, existing.DonationReference);

            return Result.Success("This payment was already recorded.");
        }

        var now = clock.UtcNow;
        var capturedAmount = paymentEvent.Amount ?? attempt.RequestedAmount;

        attempt.Status = PaymentAttemptStatus.Succeeded;

        // A COPY, NOT THE INSTANCE ITSELF. `capturedAmount` is already owned by either the
        // PaymentEvent or the PaymentAttempt it was read from, and MoneyValue is an EF owned type
        // whose identity is its owner - so storing the same object under a second owner makes EF
        // track one instance as two entities and emit an update for both. See MoneyValue.Copy:
        // this is what threw DbUpdateConcurrencyException and rolled back a captured donation.
        attempt.CapturedAmount = capturedAmount.Copy();
        attempt.CapturedAtUtc = paymentEvent.OccurredAtUtc;

        var reference = await MintDonationReferenceAsync(cancellationToken);

        if (reference.IsFailure)
        {
            return Result.Failure<string>(reference.Error!);
        }

        var donation = new Donation
        {
            TenantId = intent.TenantId,
            BusinessUnitId = intent.BusinessUnitId,
            DonationReference = reference.Value!,
            DonationIntentId = intent.Id,
            PaymentAttemptId = attempt.Id,
            DonorId = intent.DonorId,
            CampaignId = intent.CampaignId,
            // Its own instance, for the same reason as the attempt above.
            Amount = capturedAmount.Copy(),
            RefundedAmount = MoneyValue.Zero(capturedAmount.CurrencyCode),
            CurrencyId = intent.CurrencyId,

            // The donor AS AT THE DONATION DATE. A receipt is a tax document and has to show what
            // was true on the day, so this is copied rather than joined.
            DonorName = intent.DonorName,
            DonorEmail = intent.Email,
            DonorMobile = intent.Mobile,
            DonorTaxIdentifier = intent.TaxIdentifier,
            DonorAddress = FlattenAddress(intent),

            Status = DonationStatus.Recorded,
            DonatedAtUtc = paymentEvent.OccurredAtUtc,
            MethodType = attempt.MethodType,
            GatewayReference = attempt.GatewayReference,
            SettlementStatus = SettlementStatus.Pending,
            ReconciliationStatus = ReconciliationStatus.Unreconciled,

            // Attribution, denormalised so donation reporting never joins back through the intent.
            SourceType = intent.SourceType,
            TrackingAssetId = intent.TrackingAssetId,
            LeadId = intent.LeadId
        };

        await donations.AddDonationAsync(donation, cancellationToken);

        intent.Status = DonationIntentStatus.Paid;
        intent.PaymentLinkUrl = null;
        intent.FailureReason = null;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.DonationRecorded,
            nameof(Donation),
            donation.Id,
            donation.TenantId,
            AuditResult.Succeeded,
            new { donation.DonationReference, intent.IntentReference, Amount = capturedAmount.ToString() },
            cancellationToken);

        // The money is now safe. Everything below is best-effort.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await EnsureDonorAsync(intent, donation, cancellationToken);
        await ConvertLeadAsync(intent, donation, cancellationToken);
        await IssueReceiptIfConfiguredAsync(donation, cancellationToken);

        logger.LogInformation(
            "Donation {DonationReference} recorded for organisation {TenantId} from intent "
            + "{IntentReference}.", donation.DonationReference, donation.TenantId, intent.IntentReference);

        return Result.Success($"Donation {donation.DonationReference} recorded.");
    }

    /// <summary>
    /// Sections 15 and 17: find or create the donor, then create their account and invite them.
    ///
    /// SECTION 18 IS THE OTHER HALF: an EXISTING donor gets a new donation and no second donor
    /// record and no second account. That is the <c>match is not null</c> branch below, and it is
    /// the reason this method is "ensure" rather than "create".
    ///
    /// EVERY FAILURE HERE IS SWALLOWED AND LOGGED. The money is already taken; an invitation that
    /// would not send is a follow-up task, not a reason to unwind a gift.
    /// </summary>
    private async Task EnsureDonorAsync(
        DonationIntent intent, Donation donation, CancellationToken cancellationToken)
    {
        try
        {
            var match = await donorDirectory.FindByEmailAsync(
                intent.TenantId, intent.NormalisedEmail, cancellationToken);

            if (match is null)
            {
                // Section 15: the intent is the authoritative source for the donor's details.
                match = await donorDirectory.CreateDonorAsync(
                    new CreateDonorFromIntentRequest(
                        intent.TenantId,
                        intent.BusinessUnitId,
                        intent.DonorName,
                        intent.Email,
                        intent.NormalisedEmail,
                        intent.Mobile,
                        intent.TaxIdentifier,
                        intent.AddressLine1,
                        intent.AddressLine2,
                        intent.CountryId,
                        intent.StateId,
                        intent.CityId,
                        intent.PostalCode,
                        intent.LeadId),
                    cancellationToken);

                await audit.WriteAnonymousAsync(
                    AuditActionCodes.DonorCreatedFromIntent,
                    "Donor",
                    match.DonorId,
                    intent.TenantId,
                    AuditResult.Succeeded,
                    new { intent.IntentReference, donation.DonationReference },
                    cancellationToken);
            }

            donation.DonorId = match.DonorId;
            intent.DonorId = match.DonorId;

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Section 17: an account and an invitation, only where the donor has neither. No
            // password is set - the donor chooses one through the activation link.
            if (_settings.CreateDonorAccountOnSuccess && !match.HasActiveAccount)
            {
                var account = await donorDirectory.CreateAccountAndInviteAsync(
                    new CreateDonorAccountRequest(
                        intent.TenantId,
                        match.DonorId,
                        intent.DonorName,
                        intent.Email,
                        intent.Mobile,
                        donation.DonationReference),
                    cancellationToken);

                if (account.AccountCreated)
                {
                    await audit.WriteAnonymousAsync(
                        AuditActionCodes.DonorAccountInvited,
                        "Donor",
                        match.DonorId,
                        intent.TenantId,
                        account.InvitationSent ? AuditResult.Succeeded : AuditResult.Failed,
                        new { account.UserId, account.InvitationSent, account.FailureReason },
                        cancellationToken);

                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    logger.LogWarning(
                        "The donor account for donation {DonationReference} could not be created: "
                        + "{Reason}. The donation stands and this needs following up.",
                        donation.DonationReference, account.FailureReason);
                }
            }
        }
        catch (Exception exception)
        {
            // Deliberately swallowed. See the method comment: the donation is already recorded
            // and must not be unwound because an account could not be created.
            logger.LogError(
                exception,
                "Donor setup failed for donation {DonationReference}. The donation stands and this "
                + "needs following up.", donation.DonationReference);
        }
    }

    /// <summary>
    /// Sections 16 and 28: a successful payment converts the originating lead to a donor.
    ///
    /// THE CONVERSION POINT IS THE PAYMENT, not the lead owner marking it qualified - which the
    /// brief states twice. The lead history is preserved rather than replaced: the fundraiser who
    /// captured it and the owner who worked it stay attached, which is what makes the conversion
    /// reportable.
    /// </summary>
    private async Task ConvertLeadAsync(
        DonationIntent intent, Donation donation, CancellationToken cancellationToken)
    {
        if (!intent.OriginatedFromLead || donation.DonorId is null)
        {
            return;
        }

        try
        {
            await donorDirectory.MarkLeadConvertedAsync(
                intent.TenantId, intent.LeadId!.Value, donation.DonorId.Value, cancellationToken);

            await audit.WriteAnonymousAsync(
                AuditActionCodes.LeadConverted,
                "Lead",
                intent.LeadId.Value,
                intent.TenantId,
                AuditResult.Succeeded,
                new { donation.DonationReference, donation.DonorId },
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Lead {LeadId} could not be marked converted for donation {DonationReference}. "
                + "The donation stands and this needs following up.",
                intent.LeadId, donation.DonationReference);
        }
    }

    /// <summary>
    /// Section 24: issues the receipt, where the organisation has that switched on.
    ///
    /// The receipt itself is created here in draft and ISSUED by the receipt handler, so the
    /// numbering and the document rendering live in one place rather than two.
    /// </summary>
    private async Task IssueReceiptIfConfiguredAsync(
        Donation donation, CancellationToken cancellationToken)
    {
        if (!_settings.AutoIssueReceiptOnDonation)
        {
            return;
        }

        try
        {
            var financialYear = clock.FinancialYearFor(donation.DonatedAtUtc);

            var sequence = await receipts.AllocateNextReceiptNumberAsync(
                donation.TenantId, financialYear, cancellationToken);

            var receipt = new Receipt
            {
                TenantId = donation.TenantId,
                BusinessUnitId = donation.BusinessUnitId,
                DonationId = donation.Id,
                VersionNumber = 1,
                ReceiptNumber =
                    $"{_settings.ReceiptNumberPrefix}/{financialYear}/{sequence:00000}",
                Status = ReceiptStatus.Issued,
                DeliveryStatus = ReceiptDeliveryStatus.NotSent,
                FinancialYear = financialYear,

                // THE DONATION'S AMOUNT, COPIED. Assigning `donation.Amount` directly handed the
                // Donation's own owned instance to the Receipt as well, which is the pairing EF
                // named first in its warning and the one that failed the save.
                Amount = donation.Amount.Copy(),
                DonorName = donation.DonorName,
                DonorEmail = donation.DonorEmail,
                DonorAddress = donation.DonorAddress,
                DonorTaxIdentifier = donation.DonorTaxIdentifier,

                // THE CAMPAIGN THE GIFT WAS GIVEN TO, resolved once and snapshotted onto the
                // receipt. It was never set at all, so every auto-issued receipt carried a null
                // here - which is why the register and the Payments & Receipts page showed a
                // dash in the campaign column for every successful donation, and why a printed
                // receipt could not say what the money was for.
                //
                // A NAME, NOT AN ID, AND A SNAPSHOT. The receipt is a statement about a moment:
                // a campaign renamed next year must not change what the donor's copy says.
                CampaignOrFundName = await ResolveCampaignNameAsync(donation, cancellationToken),

                IssuedAtUtc = clock.UtcNow
            };

            await receipts.AddAsync(receipt, cancellationToken);

            await audit.WriteAnonymousAsync(
                AuditActionCodes.ReceiptIssued,
                nameof(Receipt),
                receipt.Id,
                donation.TenantId,
                AuditResult.Succeeded,
                new { receipt.ReceiptNumber, donation.DonationReference },
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            await RenderAndDeliverAsync(receipt, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The receipt for donation {DonationReference} could not be issued. The donation "
                + "stands and this needs following up.", donation.DonationReference);
        }
    }

    /// <summary>
    /// The campaign's name for the receipt, or null when there is nothing to name.
    ///
    /// IT NEVER FAILS THE RECEIPT. Campaigns live in another service, so this is a call that can
    /// time out - and a receipt is a tax document that must be issued whether or not a display
    /// name could be fetched. A null campaign line on a receipt is a cosmetic loss; a receipt
    /// that was not issued because a lookup was slow is a compliance one.
    /// </summary>
    private async Task<string?> ResolveCampaignNameAsync(
        Donation donation, CancellationToken cancellationToken)
    {
        if (donation.CampaignId is null)
        {
            return null;
        }

        try
        {
            return await campaignDirectory.GetCampaignNameAsync(
                donation.TenantId, donation.CampaignId.Value, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The campaign name for donation {DonationReference} could not be resolved. The "
                + "receipt is issued without it.",
                donation.DonationReference);

            return null;
        }
    }

    /// <summary>
    /// Renders the receipt document and e-mails it to the donor.
    ///
    /// THE DOCUMENT REQUIRES THIS AND IT WAS NOT HAPPENING. Section 6: "a copy of the receipt is
    /// automatically emailed to the donor at the same time", and section 1: "A successful or
    /// failed payment always generates a Payment Receipt, and a copy is emailed to the donor."
    /// The auto-issue path created the row with <c>DeliveryStatus = NotSent</c>, wrote the audit
    /// entry and stopped - so every donation taken through the public form produced a receipt
    /// that existed only inside the application. The donor received nothing, and the Receipt
    /// Register showed a delivery state of "Not sent" that no screen offered a way to change
    /// except a manual Resend somebody had to think to press.
    ///
    /// IT MIRRORS <c>ReceiptCommandHandler.RenderAndDeliverAsync</c> rather than calling it: that
    /// one is private to a handler with its own repository and unit of work, and reaching across
    /// to it would make the payment pipeline depend on the receipt-administration handler's
    /// lifetime. The shared part - rendering and sending - is <see cref="IReceiptDocumentService"/>,
    /// which is what both of them actually use.
    ///
    /// EVERY FAILURE IS LOGGED AND SWALLOWED. The money is already taken and the receipt is
    /// validly issued the moment it is numbered; a PDF that would not render or an inbox that
    /// bounced is a follow-up task, not a reason to fail a donation that succeeded. A receipt
    /// left undelivered surfaces on the Receipt Register with its delivery state showing, and
    /// Receipt Correction can resend it.
    /// </summary>
    private async Task RenderAndDeliverAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            var rendered = await receiptDocuments.RenderAsync(receipt, cancellationToken);

            if (rendered.Succeeded)
            {
                receipt.DocumentUrl = rendered.DocumentUrl;
            }
            else
            {
                logger.LogWarning(
                    "Receipt {ReceiptNumber} could not be rendered: {Reason}. The receipt is still "
                    + "validly issued.", receipt.ReceiptNumber, rendered.FailureReason);
            }

            if (_settings.AutoDeliverReceipt && !string.IsNullOrWhiteSpace(receipt.DonorEmail))
            {
                var delivery = new ReceiptDelivery
                {
                    TenantId = receipt.TenantId,
                    BusinessUnitId = receipt.BusinessUnitId,
                    ReceiptId = receipt.Id,
                    Channel = "Email",
                    Destination = receipt.DonorEmail,
                    Status = ReceiptDeliveryStatus.Pending,
                    AttemptedAtUtc = clock.UtcNow
                };

                ReceiptDeliveryResult result;

                try
                {
                    result = await receiptDocuments.DeliverAsync(
                        receipt, "Email", receipt.DonorEmail, cancellationToken);
                }
                catch (Exception exception)
                {
                    result = new ReceiptDeliveryResult(false, null, exception.Message);
                }

                if (result.Succeeded)
                {
                    delivery.Status = ReceiptDeliveryStatus.Delivered;
                    delivery.DeliveredAtUtc = clock.UtcNow;
                    delivery.ProviderReference = result.ProviderReference;
                    receipt.DeliveryStatus = ReceiptDeliveryStatus.Delivered;
                }
                else
                {
                    delivery.Status = ReceiptDeliveryStatus.Failed;
                    delivery.FailureReason = result.FailureReason;

                    // FAILED, NOT "NOT SENT". The difference is what a person reading the
                    // register needs: nobody tried, versus somebody tried and it bounced.
                    receipt.DeliveryStatus = ReceiptDeliveryStatus.Failed;

                    logger.LogWarning(
                        "Receipt {ReceiptNumber} could not be delivered to the donor: {Reason}.",
                        receipt.ReceiptNumber, result.FailureReason);
                }

                // ADDED THROUGH THE REPOSITORY, NOT BY DROPPING IT IN THE NAVIGATION COLLECTION.
                //
                // `BaseEntity.Id` is initialised to `Guid.NewGuid()`, so a ReceiptDelivery has a
                // non-default primary key from the moment it is constructed. When EF then
                // discovers it during DetectChanges as a new child of an already-tracked Receipt,
                // it reads that set key as "this row exists" and marks the entity MODIFIED - so
                // the save issued an UPDATE against a row that had never been inserted and got
                // back "expected to affect 1 row(s), but actually affected 0".
                //
                // THAT EXCEPTION WAS THE ONE THAT UNWOUND THE DONATION. It was thrown inside this
                // best-effort block, swallowed and logged as a delivery problem - but the failed
                // entity stayed in the change tracker, so ApplyPaymentEventCommand's own save
                // retried the same impossible UPDATE, threw again outside any catch, and rolled
                // the transaction back. The receipt, the donation and the payment event all went
                // with it, for a payment the gateway had already captured.
                //
                // `AddDeliveryAsync` puts it in the context as ADDED explicitly, which is how the
                // receipt-administration handler this method mirrors has always done it. Every
                // other new entity in this service goes through a repository for the same reason.
                await receipts.AddDeliveryAsync(delivery, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Rendering or delivering receipt {ReceiptNumber} failed. The receipt is still "
                + "validly issued and this needs following up.", receipt.ReceiptNumber);
        }
    }

    private static Result<string> ApplyAuthorised(PaymentAttempt attempt)
    {
        // Authorised is not captured: the money is held, not taken. The donation is recorded on
        // capture and not before, or a released authorisation would leave phantom income.
        attempt.Status = PaymentAttemptStatus.Authorised;

        return Result.Success("Payment authorised. Awaiting capture.");
    }

    private Result<string> ApplyFailure(
        PaymentAttempt attempt, DonationIntent intent, PaymentEvent paymentEvent)
    {
        attempt.Status = PaymentAttemptStatus.Failed;
        attempt.FailedAtUtc = clock.UtcNow;
        attempt.DonorFacingMessage =
            "The payment was not completed. You have not been charged. Please try again.";

        // The INTENT goes to Failed rather than terminal: section 23 allows a retry, and a
        // terminal status would take that away.
        intent.Status = DonationIntentStatus.Failed;
        intent.FailureReason = "The payment provider declined the payment.";

        return Result.Success("Payment failed. The donor can retry.");
    }

    private static Result<string> ApplyCancelled(PaymentAttempt attempt, DonationIntent intent)
    {
        attempt.Status = PaymentAttemptStatus.Abandoned;
        intent.Status = DonationIntentStatus.Failed;

        return Result.Success("The donor abandoned the payment.");
    }

    private static Result<string> ApplyExpired(PaymentAttempt attempt, DonationIntent intent)
    {
        attempt.Status = PaymentAttemptStatus.Abandoned;
        intent.Status = DonationIntentStatus.Expired;
        intent.PaymentLinkUrl = null;

        return Result.Success("The payment link expired.");
    }

    /// <summary>
    /// Settlement: the money reached the organisation's bank account.
    ///
    /// SEPARATE FROM CAPTURE, and the gap between them is where reconciliation lives. A donation
    /// is real from capture; it is not in the bank until this event, minus the gateway's fee.
    /// </summary>
    private async Task<Result<string>> ApplySettlementAsync(
        DonationIntent intent, PaymentEvent paymentEvent, CancellationToken cancellationToken)
    {
        var donation = await donations.GetDonationByIntentAsync(intent.Id, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<string>(Error.NotFound(
                "A settlement event arrived for a payment with no recorded donation."));
        }

        donation.SettlementStatus = SettlementStatus.Settled;
        donation.SettledAtUtc = paymentEvent.OccurredAtUtc;

        if (donation.Status == DonationStatus.Recorded)
        {
            donation.Status = DonationStatus.Settled;
        }

        await audit.WriteAnonymousAsync(
            AuditActionCodes.DonationSettled,
            nameof(Donation),
            donation.Id,
            donation.TenantId,
            AuditResult.Succeeded,
            new { donation.DonationReference },
            cancellationToken);

        return Result.Success($"Donation {donation.DonationReference} settled.");
    }

    // =====================================================================================
    // Confirming an in-page checkout
    // =====================================================================================

    /// <summary>
    /// Takes the provider's signed word that a checkout completed, then confirms the outcome
    /// with the provider directly.
    /// </summary>
    /// <remarks>
    /// TWO STEPS, AND BOTH ARE NECESSARY. The signature proves the message is the provider's: it
    /// is an HMAC over the order and payment ids made with the merchant secret, which the browser
    /// has never held, so a fabricated payment id cannot be signed. What it does NOT prove is
    /// that money moved - it is a receipt for a conversation, not for a capture. So a valid
    /// signature only earns the payment id the right to be written down; the outcome still comes
    /// from <see cref="VerifyPaymentCommand"/>, which asks the provider what actually happened.
    ///
    /// THE PAYMENT ID REPLACES THE ORDER ID ON THE ATTEMPT, and that is the hinge of the whole
    /// flow. Verification resolves a reference at the provider; an order id says only what was
    /// asked for, a payment id says what was done. Writing it here is what lets every existing
    /// downstream path - the receipt, the donor account, the e-mail, Support and Safe Retry -
    /// work unchanged against a checkout payment.
    ///
    /// A BAD SIGNATURE CHANGES NOTHING AND IS AUDITED. The attempt is left exactly as it was, so
    /// the donation stays recoverable through the ordinary verification route; nothing is marked
    /// failed on the strength of a message we could not authenticate. It is the one event here
    /// worth alerting on: a genuine donor's browser does not produce one.
    /// </remarks>
    public async Task<Result<PaymentVerificationResponse>> HandleAsync(
        ConfirmCheckoutPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await donations.GetIntentByReferenceAsync(
            command.IntentReference, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<PaymentVerificationResponse>(
                Error.NotFound("That donation was not found."));
        }

        var attempt = await donations.GetLatestAttemptAsync(intent.Id, cancellationToken);

        if (attempt is null)
        {
            return Result.Failure<PaymentVerificationResponse>(
                Error.NotFound("That payment was not found."));
        }

        var account = await gatewayAccounts.GetActiveForTenantAsync(intent.TenantId, cancellationToken);

        if (account is null)
        {
            return Result.Failure<PaymentVerificationResponse>(Error.PaymentGatewayNotConfigured());
        }

        var signatureValid = paymentGateway.VerifyCheckoutSignature(
            account,
            command.Request.OrderReference,
            command.Request.PaymentReference,
            command.Request.Signature);

        if (!signatureValid)
        {
            logger.LogWarning(
                "A checkout confirmation for intent {IntentReference} failed signature "
                + "verification and was not applied.",
                intent.IntentReference);

            await audit.WriteAnonymousAsync(
                AuditActionCodes.IntentCheckoutSignatureRejected,
                nameof(PaymentAttempt),
                attempt.Id,
                intent.TenantId,
                AuditResult.Failed,
                new { intent.IntentReference, attempt.AttemptNumber },
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<PaymentVerificationResponse>(Error.Forbidden(
                "This payment could not be verified. Please contact the organisation before "
                + "trying again - do not make a second payment."));
        }

        attempt.GatewayReference = command.Request.PaymentReference;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // THE ORDINARY VERIFICATION PATH FROM HERE. It asks the provider, records the donation,
        // issues the receipt, converts the donor and sends the e-mail - none of which needs to
        // know that this payment came from a checkout rather than a link.
        return await HandleAsync(
            new VerifyPaymentCommand(
                new VerifyPaymentRequest(IntentReference: intent.IntentReference)),
            cancellationToken);
    }

    // =====================================================================================
    // Verification - SCR-PAY-002
    // =====================================================================================

    /// <summary>
    /// Asks the gateway what actually happened.
    ///
    /// THE ANSWER MAY BE "STILL PENDING", and that is a legitimate outcome rather than a failure.
    /// It is reported with PAYMENT_VERIFICATION_PENDING and a 202, so the donor-facing page shows
    /// "we are still confirming" rather than "it failed" - because a donor told it failed tries
    /// again, and if the first attempt actually succeeded they have now given twice.
    /// </summary>
    public async Task<Result<PaymentVerificationResponse>> HandleAsync(
        VerifyPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        PaymentAttempt? attempt;
        DonationIntent? intent;

        if (request.PaymentAttemptId.HasValue)
        {
            attempt = await donations.GetAttemptAsync(request.PaymentAttemptId.Value, cancellationToken);
            intent = attempt is null
                ? null
                : await donations.GetIntentAsync(attempt.DonationIntentId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.IntentReference))
        {
            intent = await donations.GetIntentByReferenceAsync(request.IntentReference, cancellationToken);
            attempt = intent is null
                ? null
                : await donations.GetLatestAttemptAsync(intent.Id, cancellationToken);
        }
        else
        {
            return Result.Failure<PaymentVerificationResponse>(Error.Validation(
                "Name either a donation reference or a payment attempt.",
                [new ValidationError("intentReference", "Supply a donation reference.")]));
        }

        if (intent is null || attempt is null)
        {
            return Result.Failure<PaymentVerificationResponse>(
                Error.NotFound("That payment was not found."));
        }

        var account = await gatewayAccounts.GetActiveForTenantAsync(intent.TenantId, cancellationToken);

        if (account is null || string.IsNullOrWhiteSpace(attempt.GatewayReference))
        {
            // Nothing to verify against. The local state is the best answer available.
            return BuildVerificationResponse(intent, attempt, null, cancellationToken);
        }

        GatewayVerificationResult verification;

        try
        {
            verification = await paymentGateway.VerifyPaymentAsync(
                account, attempt.GatewayReference, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Verification failed for attempt {AttemptId} on intent {IntentReference}.",
                attempt.Id, intent.IntentReference);

            return Result.Failure<PaymentVerificationResponse>(Error.PaymentGatewayUnavailable(
                "We could not reach the payment provider to confirm this payment. "
                + "Please try again shortly - do not make a second payment."));
        }

        // The gateway is authoritative. If it says captured and we have no donation, the capture
        // webhook was lost and this is where it gets put right.
        if (verification.Status == PaymentAttemptStatus.Succeeded
            && await donations.GetDonationByIntentAsync(intent.Id, cancellationToken) is null)
        {
            logger.LogWarning(
                "Verification found a captured payment with no recorded donation for intent "
                + "{IntentReference}. Recording it now.", intent.IntentReference);

            attempt.MethodType = verification.MethodType;
            attempt.MaskedInstrument = verification.MaskedInstrument;

            // THE RECONSTRUCTED EVENT IS LOOKED UP BEFORE IT IS CREATED, exactly as the webhook
            // path above looks up a redelivery before storing one.
            //
            // WHY IT HAS TO BE. `GatewayEventId` is deterministic here - "verify:{gateway
            // reference}" - and (gateway_name, gateway_event_id) is UNIQUE. So the second
            // verification of the same payment inserted a row the database had already, and
            // SaveChangesAsync threw DbUpdateException from outside every try/catch on this path.
            //
            // A SECOND VERIFICATION IS THE NORMAL CASE, NOT AN EDGE ONE. The donor's result page
            // polls this endpoint four times while a capture settles, Support & Retry offers a
            // Verify button, and Safe Retry verifies before it re-pays. Any of those, on a
            // payment whose donation had not been recorded, hit the duplicate - and the donor was
            // shown "An error occurred while saving the entity changes. See the inner exception
            // for details." on the page that was meant to confirm their gift.
            var eventReference = $"verify:{attempt.GatewayReference}";

            var reconstructed = await paymentEvents.FindByGatewayEventIdAsync(
                account.GatewayName, eventReference, cancellationToken);

            if (reconstructed is null)
            {
                reconstructed = new PaymentEvent
                {
                    TenantId = intent.TenantId,
                    BusinessUnitId = intent.BusinessUnitId,
                    PaymentAttemptId = attempt.Id,
                    DonationIntentId = intent.Id,
                    EventType = PaymentEventType.Captured,
                    Status = PaymentEventStatus.Pending,
                    GatewayName = account.GatewayName,

                    // Marked as reconstructed, so the queue shows this came from verification
                    // rather than from the provider - which matters when somebody asks why there
                    // is no matching webhook.
                    GatewayEventId = eventReference,

                    GatewayReference = attempt.GatewayReference,

                    // `CapturedAmount` is freshly built from the gateway response and owned by
                    // nobody; `RequestedAmount` belongs to the attempt, so that branch is copied.
                    Amount = verification.CapturedAmount ?? attempt.RequestedAmount.Copy(),
                    OccurredAtUtc = verification.CapturedAtUtc ?? clock.UtcNow,
                    ReceivedAtUtc = clock.UtcNow,
                    SignatureVerified = true
                };

                await paymentEvents.AddAsync(reconstructed, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // AN EVENT THAT HAS ALREADY BEEN APPLIED IS NOT APPLIED AGAIN. Re-applying is refused
            // by the handler anyway, but asking it to is how a retry turns into an error the
            // caller has to interpret.
            if (reconstructed.Status != PaymentEventStatus.Processed)
            {
                // THE OUTCOME IS READ AND LOGGED, NOT DISCARDED - the same treatment the webhook
                // path gives it. This was `await HandleAsync(...)` with the Result thrown away, so
                // a capture that could not be applied left the donation unrecorded and said
                // nothing anywhere, and any exception from inside it escaped this method
                // entirely and became a 500 on the donor's result page.
                var applied = await HandleAsync(
                    new ApplyPaymentEventCommand(reconstructed.Id), cancellationToken);

                if (applied.IsFailure)
                {
                    logger.LogError(
                        "The reconstructed capture {GatewayEventId} for intent {IntentReference} "
                        + "was stored but could not be applied: {Message}",
                        eventReference, intent.IntentReference, applied.Error!.Message);
                }
            }

            intent = await donations.GetIntentAsync(intent.Id, cancellationToken) ?? intent;
            attempt = await donations.GetAttemptAsync(attempt.Id, cancellationToken) ?? attempt;
        }
        else
        {
            attempt.Status = verification.Status;
            attempt.GatewayResultCode = verification.ResultCode;
            attempt.GatewayMessage = verification.Message;
        }

        await audit.WriteAnonymousAsync(
            AuditActionCodes.PaymentVerified,
            nameof(PaymentAttempt),
            attempt.Id,
            intent.TenantId,
            AuditResult.Succeeded,
            new { intent.IntentReference, Outcome = verification.Status.ToString() },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildVerificationResponse(intent, attempt, verification, cancellationToken);
    }

    // =====================================================================================
    // Safe retry - section 23, SCR-PAY-007
    // =====================================================================================

    /// <summary>
    /// Retries a payment, safely.
    ///
    /// "SAFELY" MEANS ONE THING: the previous attempt is VERIFIED WITH THE GATEWAY FIRST, and if
    /// it actually succeeded the retry is refused. That is the entire difference between helping
    /// a donor whose card was declined and charging one who has already paid - and it is why this
    /// is a distinct operation with its own permission rather than a second click of Pay.
    /// </summary>
    public async Task<Result<SafeRetryResponse>> HandleAsync(
        SafeRetryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await donations.GetIntentAsync(command.IntentId, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<SafeRetryResponse>(Error.NotFound("That donation was not found."));
        }

        if (intent.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<SafeRetryResponse>(Error.Concurrency());
        }

        await audit.WriteAsync(
            AuditActionCodes.PaymentSafeRetryRequested,
            nameof(DonationIntent),
            intent.Id,
            new { intent.IntentReference },
            command.Request.Reason,
            cancellationToken);

        if (intent.Status == DonationIntentStatus.Paid)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new SafeRetryResponse(
                intent.Id,
                intent.IntentReference,
                "AlreadyPaid",
                "This donation has already been paid. No retry is needed.",
                null,
                intent.Status,
                intent.AttemptCount,
                PermittedActions(intent));
        }

        // THE VERIFICATION STEP. Without it this is just a retry button with a longer name.
        var latest = await donations.GetLatestAttemptAsync(intent.Id, cancellationToken);

        if (latest is not null && latest.NeedsVerification)
        {
            var verified = await HandleAsync(
                new VerifyPaymentCommand(new VerifyPaymentRequest(PaymentAttemptId: latest.Id)),
                cancellationToken);

            if (verified.IsFailure)
            {
                return Result.Failure<SafeRetryResponse>(verified.Error!);
            }

            var refreshed = await donations.GetIntentAsync(intent.Id, cancellationToken) ?? intent;

            if (refreshed.Status == DonationIntentStatus.Paid)
            {
                return new SafeRetryResponse(
                    refreshed.Id,
                    refreshed.IntentReference,
                    "AlreadyPaid",
                    "Verification found this payment had already succeeded. No retry was made.",
                    null,
                    refreshed.Status,
                    refreshed.AttemptCount,
                    PermittedActions(refreshed));
            }

            var stillPending = await donations.GetLatestAttemptAsync(refreshed.Id, cancellationToken);

            if (stillPending is not null && stillPending.Status == PaymentAttemptStatus.Pending)
            {
                return new SafeRetryResponse(
                    refreshed.Id,
                    refreshed.IntentReference,
                    "StillPending",
                    "The provider has not settled this payment yet. Wait before retrying, so the "
                    + "donor is not charged twice.",
                    null,
                    refreshed.Status,
                    refreshed.AttemptCount,
                    PermittedActions(refreshed));
            }

            intent = refreshed;
        }

        if (intent.IsTerminal)
        {
            return Result.Failure<SafeRetryResponse>(Error.InvalidTransition(
                $"This donation is {intent.Status} and cannot be retried."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SafeRetryResponse(
            intent.Id,
            intent.IntentReference,
            "Retried",
            "The previous attempt was confirmed as unsuccessful. A new payment link can be issued.",
            null,
            intent.Status,
            intent.AttemptCount,
            PermittedActions(intent));
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    private Result<PaymentVerificationResponse> BuildVerificationResponse(
        DonationIntent intent,
        PaymentAttempt attempt,
        GatewayVerificationResult? verification,
        CancellationToken cancellationToken)
    {
        var state = attempt.Status switch
        {
            PaymentAttemptStatus.Succeeded => "Confirmed",
            PaymentAttemptStatus.Failed or PaymentAttemptStatus.Abandoned => "Failed",
            _ => "Pending"
        };

        var receiptEligible = intent.Status == DonationIntentStatus.Paid;

        var history = intent.Attempts
            .OrderByDescending(item => item.AttemptNumber)
            .Select(item => new PaymentVerificationHistoryRow(
                $"Attempt {item.AttemptNumber}: {PaymentMappingConfig.Describe(item.Status)}",
                item.GatewayResultCode ?? item.GatewayName,
                item.InitiatedAtUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        return Result.Success(new PaymentVerificationResponse(
            intent.IntentReference,
            attempt.RequestedAmount.ToResponse(),
            state,
            verification is null ? attempt.CapturedAtUtc : clock.UtcNow,
            attempt.GatewayReference,
            receiptEligible ? "Eligible" : "Not yet eligible",
            null,

            // The correlation id of THIS request, which is what the donor quotes to support and
            // what ties their call to the log line for the verification.
            currentUser.CorrelationId,

            history,
            PermittedActions(intent),

            // THE ATTEMPT'S PROVIDER, not the organisation's current one - see the DTO. An
            // attempt that never reached a provider carries no name, and the result page shows
            // nothing rather than inventing one.
            attempt.GatewayName,

            // WHERE THE DONOR GOES NEXT IS DECIDED FROM THESE TWO. A lead is converted on the
            // confirming payment and has no password yet; a recognised donor already has one.
            intent.OriginatedFromLead,
            intent.ExistingDonorMatched ?? false));
    }

    private async Task<Result<string>> MintDonationReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReferenceAttempts; attempt++)
        {
            var candidate = references.NewDonationReference();

            if (!await donations.DonationReferenceExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return Result.Failure<string>(Error.Dependency(
            "A unique donation reference could not be generated."));
    }

    /// <summary>Flattens the intent's address for the donation snapshot and the receipt.</summary>
    private static string? FlattenAddress(DonationIntent intent)
    {
        var parts = new[] { intent.AddressLine1, intent.AddressLine2, intent.PostalCode }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private IReadOnlyList<string> PermittedActions(DonationIntent intent) =>
        DonationMappingConfig.PermittedActionsFor(
            intent, currentUser.HasPermission, clock.UtcNow, _settings.MaximumAttemptsBeforeSupport);
}
