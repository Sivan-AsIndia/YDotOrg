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
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Donations.Commands.ManageIntent;

/// <summary>Starts a donation. Reached from every entry channel - sections 19 to 22.</summary>
public sealed record CreateDonationIntentCommand(CreateDonationIntentRequest Request);

/// <summary>Section 12: does this e-mail already belong to a donor in THIS organisation?</summary>
public sealed record CheckExistingDonorCommand(string IntentReference);

/// <summary>Issues a payment link and opens an attempt.</summary>
public sealed record CreatePaymentLinkCommand(string IntentReference, CreatePaymentLinkRequest Request);

/// <summary>
/// Opens an in-page checkout session on an intent - the flow a donor sitting in front of the
/// form actually wants, as against a link e-mailed to somebody who is not at a screen.
/// </summary>
public sealed record CreateCheckoutSessionCommand(
    string IntentReference, CreateCheckoutSessionRequest Request);

/// <summary>Cancels an intent. Staff only.</summary>
public sealed record CancelDonationIntentCommand(Guid IntentId, CancelDonationIntentRequest Request);

/// <summary>Sends the payment link again.</summary>
public sealed record ResendPaymentLinkCommand(Guid IntentId, long ExpectedVersion);

/// <summary>
/// The donation intent: creating one, checking whether the donor is already known, and issuing
/// the payment link.
///
/// SECTION 22 IS THE SHAPE OF THIS CLASS. Nine entry channels - a fundraiser's lead link, a QR
/// code, a website button, an e-mail, a social post - converge on ONE payment decision. There is
/// no per-channel branch anywhere below: the channel is recorded as attribution on the intent
/// and everything downstream reads the intent.
///
/// THE EXISTING-DONOR CHECK IS ORGANISATION-SCOPED, WHICH SECTION 26 STATES TWICE. The lookup is
/// (OrganisationId, NormalisedEmail) and never e-mail alone: the same person may give to two
/// charities on this platform and be a known donor to one and a complete stranger to the other.
/// Getting that wrong would show one charity that a person is "already their donor" on the
/// strength of a relationship with a different charity.
///
/// MOST OF THIS RUNS WITH NO AUTHENTICATED USER. The public donation flow is a stranger with a
/// QR code, so the Organisation comes from the tracking reference or the campaign rather than
/// from a token - and the audit rows are written through the anonymous path so they still record
/// which Organisation the action belonged to.
/// </summary>
public sealed class DonationIntentCommandHandler(
    IDonationRepository donations,
    IGatewayAccountRepository gatewayAccounts,
    IDonorDirectory donorDirectory,
    IPaymentGateway paymentGateway,
    IReferenceGenerator references,
    IAuditWriter audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<PaymentSettings> paymentOptions,
    IUnitOfWork unitOfWork,
    ILogger<DonationIntentCommandHandler> logger)
{
    private readonly PaymentSettings _settings = paymentOptions.Value;

    /// <summary>How many times a reference collision is retried before giving up.</summary>
    private const int ReferenceAttempts = 5;

    // =====================================================================================
    // Create - sections 11 and 22
    // =====================================================================================

    public async Task<Result<DonationIntentResponse>> HandleAsync(
        CreateDonationIntentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        // The Organisation has already been resolved by the middleware - from the tracking
        // reference, the campaign or the public organisation slug in the route. It is never
        // taken from the request body.
        if (!tenantContext.HasTenant)
        {
            return Result.Failure<DonationIntentResponse>(Error.TenantSelectionRequired(
                "This donation link is not linked to an organisation."));
        }

        var tenantId = tenantContext.RequireTenantId();
        var now = clock.UtcNow;

        var normalisedEmail = request.Email.Trim().ToLowerInvariant();

        // A DONOR WHO DOUBLE-SUBMITS THE FORM GETS THEIR EXISTING INTENT BACK, not a second one.
        // Two intents for one gift means two payment links, and a donor who pays both has given
        // twice - which is a refund case that need never have existed.
        var existing = await donations.FindOpenIntentAsync(
            tenantId, normalisedEmail, request.Amount, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Reusing open intent {IntentReference} for a repeated submission.",
                existing.IntentReference);

            return existing.ToResponse(
                campaignName: null,
                PermittedActions(existing, now));
        }

        var reference = await MintIntentReferenceAsync(cancellationToken);

        if (reference.IsFailure)
        {
            return Result.Failure<DonationIntentResponse>(reference.Error!);
        }

        var intent = request.ToEntity(
            tenantId,
            tenantContext.BusinessUnitId,
            reference.Value!,

            // Resolved by the middleware from the tracking reference it was given. Null when the
            // donor arrived by a route that carries no tracking.
            trackingAssetId: null,
            now);

        await donations.AddIntentAsync(intent, cancellationToken);

        await audit.WriteAnonymousAsync(
            AuditActionCodes.IntentCreated,
            nameof(DonationIntent),
            intent.Id,
            tenantId,
            AuditResult.Succeeded,
            new
            {
                intent.IntentReference,
                Source = intent.SourceType.ToString(),
                intent.CampaignId,
                Amount = intent.Amount.ToString()
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Donation intent {IntentReference} created for organisation {TenantId} from {Source}.",
            intent.IntentReference, tenantId, intent.SourceType);

        return intent.ToResponse(campaignName: null, PermittedActions(intent, now));
    }

    // =====================================================================================
    // The existing-donor check - sections 12, 13, 14 and 26
    // =====================================================================================

    /// <summary>
    /// Section 12: is this e-mail already a donor for THIS organisation?
    ///
    /// THE ANSWER IS A BRANCH, NOT AN ERROR. A match means the donor signs in and the intent is
    /// preserved across the redirect - section 13 is explicit that the donation intent must not
    /// be lost. No match means they continue straight to payment without creating a password
    /// first, which is section 14.
    ///
    /// THE RESPONSE MASKS THE E-MAIL even though the caller just typed it. The endpoint is
    /// reachable with any address, so an unmasked echo would turn it into an oracle: type an
    /// address, learn whether that person gives to this charity.
    /// </summary>
    public async Task<Result<ExistingDonorCheckResponse>> HandleAsync(
        CheckExistingDonorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await donations.GetIntentByReferenceAsync(command.IntentReference, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<ExistingDonorCheckResponse>(
                Error.NotFound("That donation was not found."));
        }

        if (intent.Status == DonationIntentStatus.Paid)
        {
            return Result.Failure<ExistingDonorCheckResponse>(Error.IntentAlreadyPaid());
        }

        // SECTION 26, THE WHOLE RULE IN ONE CALL: organisation AND normalised e-mail, never
        // e-mail alone.
        var match = await donorDirectory.FindByEmailAsync(
            intent.TenantId, intent.NormalisedEmail, cancellationToken);

        intent.ExistingDonorMatched = match is not null;
        intent.ExistingDonorCheckedAtUtc = clock.UtcNow;
        intent.DonorId = match?.DonorId;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.IntentExistingDonorMatched,
            nameof(DonationIntent),
            intent.Id,
            intent.TenantId,
            AuditResult.Succeeded,
            new { intent.IntentReference, Matched = match is not null },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var maskedEmail = PaymentMappingConfig.MaskEmail(intent.Email, canSeeSensitive: false);

        if (match is null)
        {
            // Section 14: continue without signing in. No password is created at this point.
            return new ExistingDonorCheckResponse(
                ExistingDonorFound: false,
                MaskedEmail: maskedEmail,
                HasActiveAccount: false,
                NextStep: "Continue",
                Message: "Continue to payment. We will set up your account after your donation.");
        }

        // Section 13: sign in, then come back to this intent.
        return new ExistingDonorCheckResponse(
            ExistingDonorFound: true,
            MaskedEmail: maskedEmail,
            HasActiveAccount: match.HasActiveAccount,
            NextStep: match.HasActiveAccount ? "SignIn" : "Continue",
            Message: match.HasActiveAccount
                ? "You already have an account with this organisation. Please sign in to continue."

                // A donor record with no ACTIVATED account cannot sign in, so sending them to a
                // sign-in screen would strand them. They continue, and the payment reuses the
                // donor record that already exists rather than creating a second.
                : "We recognise you. Continue to payment - your existing record will be used.");
    }

    // =====================================================================================
    // The payment link
    // =====================================================================================

    /// <summary>
    /// Issues a payment link and opens an attempt.
    ///
    /// A LINK IS THE ASYNCHRONOUS HALF OF THE FLOW. It is a page hosted by the provider, which
    /// also e-mails it, so it suits a donor who is not in front of the form right now - a pledge
    /// followed up later, a reminder, a donor who asked to be sent something. Somebody who IS at
    /// the form gets <see cref="CreateCheckoutSessionCommand"/> instead, which never sends them
    /// away from us.
    /// </summary>
    public async Task<Result<PaymentLinkResponse>> HandleAsync(
        CreatePaymentLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var opened = await BeginPaymentAttemptAsync(
            command.IntentReference, command.Request.ExpectedVersion, cancellationToken);

        if (opened.IsFailure)
        {
            return Result.Failure<PaymentLinkResponse>(opened.Error!);
        }

        var (intent, account, attempt, idempotencyKey, now) = opened.Value!;

        GatewayLinkResult link;

        try
        {
            link = await paymentGateway.CreatePaymentLinkAsync(
                account, intent, idempotencyKey, cancellationToken);
        }
        catch (Exception exception)
        {
            await RecordAttemptTimedOutAsync(intent, attempt, exception, cancellationToken);

            return Result.Failure<PaymentLinkResponse>(Error.PaymentGatewayUnavailable());
        }

        if (!link.Succeeded)
        {
            await RecordAttemptFailedAsync(
                intent, attempt, link.FailureCode, link.FailureMessage, cancellationToken);

            return Result.Failure<PaymentLinkResponse>(Error.PaymentDeclined());
        }

        var expiresAt = link.ExpiresAtUtc
                        ?? now.AddMinutes(account.PaymentLinkValidityMinutes > 0
                            ? account.PaymentLinkValidityMinutes
                            : _settings.DefaultPaymentLinkValidityMinutes);

        attempt.Status = PaymentAttemptStatus.Pending;
        attempt.GatewayReference = link.GatewayReference;

        intent.PaymentLinkUrl = link.PaymentLinkUrl;
        intent.PaymentLinkExpiresAtUtc = expiresAt;
        intent.Status = DonationIntentStatus.AwaitingPayment;
        intent.FailureReason = null;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.IntentPaymentLinkCreated,
            nameof(DonationIntent),
            intent.Id,
            intent.TenantId,
            AuditResult.Succeeded,
            new { intent.IntentReference, attempt.AttemptNumber, account.GatewayName },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PaymentLinkResponse(
            intent.Id,
            intent.IntentReference,
            link.PaymentLinkUrl!,
            expiresAt,
            intent.Amount.ToResponse(),
            account.GatewayName,
            attempt.AttemptNumber);
    }

    // =====================================================================================
    // The in-page checkout session
    // =====================================================================================

    /// <summary>
    /// Opens a checkout session and an attempt, so the donor pays without leaving this page.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS ALONGSIDE THE LINK. The link flow sends the donor to the provider's own
    /// page and asks the provider to e-mail them; for anyone already filling in the form - a
    /// donor with the page open, or a staff member entering a gift with the donor on the phone -
    /// that is the wrong shape. They pressed Submit; the payment form should open. So this
    /// creates an ORDER at the provider and hands the browser its id, the provider's script draws
    /// its form over our page, and the donation is confirmed and shown a result without the donor
    /// ever being handed off.
    ///
    /// IT SHARES EVERY GUARD WITH THE LINK, deliberately and through the same helper: the version
    /// check, the already-paid and cancelled states, the in-progress check that stops a double
    /// click opening two attempts, the gateway account, the settlement-currency match, and an
    /// attempt written to disk BEFORE the provider is called. A second payment route with its own
    /// slightly different copy of those rules is how a donor gets charged twice.
    ///
    /// A PROVIDER THAT CANNOT DO THIS IS NOT AN ERROR. It answers NotSupported, and this reports
    /// it as such so the caller falls back to a payment link. The attempt is released rather than
    /// left in progress, or the fallback would immediately be refused as a double submit.
    /// </remarks>
    public async Task<Result<CheckoutSessionResponse>> HandleAsync(
        CreateCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var opened = await BeginPaymentAttemptAsync(
            command.IntentReference, command.Request.ExpectedVersion, cancellationToken);

        if (opened.IsFailure)
        {
            return Result.Failure<CheckoutSessionResponse>(opened.Error!);
        }

        var (intent, account, attempt, idempotencyKey, _) = opened.Value!;

        GatewayCheckoutSession session;

        try
        {
            session = await paymentGateway.CreateCheckoutSessionAsync(
                account, intent, idempotencyKey, cancellationToken);
        }
        catch (Exception exception)
        {
            await RecordAttemptTimedOutAsync(intent, attempt, exception, cancellationToken);

            return Result.Failure<CheckoutSessionResponse>(Error.PaymentGatewayUnavailable());
        }

        // NOT SUPPORTED IS A ROUTING ANSWER, NOT A FAILED PAYMENT. The attempt is rolled back to
        // an unused state and the intent returned to where it was, so the payment-link call the
        // caller makes next is not refused as a second attempt against an in-progress intent.
        if (!session.Succeeded
            && string.Equals(
                session.FailureCode,
                GatewayCheckoutSession.NotSupportedCode,
                StringComparison.Ordinal))
        {
            await ReleaseAttemptAsync(intent, attempt, cancellationToken);

            return Result.Failure<CheckoutSessionResponse>(Error.Dependency(
                "This organisation's payment provider does not support an in-page checkout."));
        }

        if (!session.Succeeded)
        {
            await RecordAttemptFailedAsync(
                intent, attempt, session.FailureCode, session.FailureMessage, cancellationToken);

            return Result.Failure<CheckoutSessionResponse>(Error.PaymentDeclined());
        }

        // THE ORDER ID IS THE ATTEMPT'S GATEWAY REFERENCE, until the browser comes back with a
        // payment id and replaces it. Recording it now is what lets Support & Retry ask the
        // provider what happened to a donor who closed the tab mid-payment.
        attempt.Status = PaymentAttemptStatus.Pending;
        attempt.GatewayReference = session.OrderReference;

        intent.Status = DonationIntentStatus.AwaitingPayment;
        intent.FailureReason = null;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.IntentCheckoutOpened,
            nameof(DonationIntent),
            intent.Id,
            intent.TenantId,
            AuditResult.Succeeded,
            new { intent.IntentReference, attempt.AttemptNumber, account.GatewayName },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CheckoutSessionResponse(
            intent.Id,
            intent.IntentReference,
            account.GatewayName,
            session.OrderReference!,
            session.PublicKey!,
            session.AmountMinorUnits,
            session.CurrencyCode ?? intent.Amount.CurrencyCode,
            intent.Amount.ToResponse(),
            intent.DonorName,
            intent.Email,
            intent.Mobile,
            $"Donation {intent.IntentReference}",
            attempt.AttemptNumber);
    }

    // =====================================================================================
    // What both payment routes share
    // =====================================================================================

    /// <summary>Everything a gateway call needs, once the guards have passed.</summary>
    private sealed record OpenedAttempt(
        DonationIntent Intent,
        PaymentGatewayAccount Account,
        PaymentAttempt Attempt,
        string IdempotencyKey,
        DateTimeOffset Now);

    /// <summary>
    /// Checks an intent is payable, resolves the gateway account and opens an attempt against it.
    ///
    /// THE ATTEMPT IS CREATED AND SAVED BEFORE THE GATEWAY IS CALLED, and the ordering is
    /// deliberate. If the call times out we still have a local record with an idempotency key,
    /// which is what lets Payment Support verify what actually happened instead of guessing - and
    /// what makes a retry safe rather than a second charge.
    ///
    /// IT IS SHARED BY BOTH PAYMENT ROUTES ON PURPOSE. The link and the in-page checkout differ
    /// only in which provider call they make; every rule about whether this donation may be paid
    /// at all is the same, and two copies of it would eventually disagree.
    /// </summary>
    private async Task<Result<OpenedAttempt>> BeginPaymentAttemptAsync(
        string intentReference, long expectedVersion, CancellationToken cancellationToken)
    {
        var intent = await donations.GetIntentByReferenceAsync(intentReference, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<OpenedAttempt>(Error.NotFound("That donation was not found."));
        }

        var guard = GuardPayable(intent, expectedVersion);

        if (guard.IsFailure)
        {
            return Result.Failure<OpenedAttempt>(guard.Error!);
        }

        // Guards the double click: a second attempt while one is genuinely in flight is how a
        // donor ends up on two payment forms at once.
        if (intent.Status == DonationIntentStatus.PaymentInProgress)
        {
            return Result.Failure<OpenedAttempt>(Error.PaymentInProgress());
        }

        var account = await gatewayAccounts.GetActiveForTenantAsync(intent.TenantId, cancellationToken);

        if (account is null)
        {
            logger.LogError(
                "Organisation {TenantId} has no active gateway account, so intent {IntentReference} "
                + "cannot be paid.", intent.TenantId, intent.IntentReference);

            return Result.Failure<OpenedAttempt>(Error.PaymentGatewayNotConfigured());
        }

        // The currency the campaign asks for has to be one the merchant account actually settles
        // in, or the gateway takes the money and the charity cannot be paid out.
        if (!string.Equals(
                account.SettlementCurrencyCode, intent.Amount.CurrencyCode, StringComparison.Ordinal))
        {
            return Result.Failure<OpenedAttempt>(Error.Dependency(
                $"This organisation cannot accept {intent.Amount.CurrencyCode}. "
                + $"Its payment account settles in {account.SettlementCurrencyCode}."));
        }

        var now = clock.UtcNow;
        var idempotencyKey = references.NewIdempotencyKey();

        var attempt = new PaymentAttempt
        {
            TenantId = intent.TenantId,
            BusinessUnitId = intent.BusinessUnitId,
            DonationIntentId = intent.Id,
            AttemptNumber = intent.AttemptCount + 1,
            Status = PaymentAttemptStatus.Initiated,
            GatewayName = account.GatewayName,

            // A COPY OF THE INTENT'S AMOUNT, not the instance. MoneyValue is an EF owned type, so
            // the same object under two owners is tracked as two entities and written twice. This
            // assignment was the ORIGIN of the shared instance that later reached the attempt, the
            // event, the donation and the receipt, and made recording a captured payment fail with
            // DbUpdateConcurrencyException. See MoneyValue.Copy.
            RequestedAmount = intent.Amount.Copy(),
            InitiatedAtUtc = now,
            IdempotencyKey = idempotencyKey,
            DonorIpAddress = currentUser.IpAddress,
            DonorUserAgent = currentUser.UserAgent
        };

        await donations.AddAttemptAsync(attempt, cancellationToken);

        intent.AttemptCount = attempt.AttemptNumber;
        intent.LastAttemptAtUtc = now;
        intent.Status = DonationIntentStatus.PaymentInProgress;

        // SAVED BEFORE THE GATEWAY CALL. If the provider times out, the attempt and its
        // idempotency key are already on disk - which is the entire basis of safe retry.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new OpenedAttempt(intent, account, attempt, idempotencyKey, now);
    }

    /// <summary>
    /// The provider could not be reached at all.
    ///
    /// THE ATTEMPT IS TimedOut, NOT Failed, and the INTENT goes to Failed rather than staying in
    /// progress. The donor is not left looking at a spinner, but the attempt's outcome is
    /// genuinely unknown and must be verified before anything retries it.
    /// </summary>
    private async Task RecordAttemptTimedOutAsync(
        DonationIntent intent,
        PaymentAttempt attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "The payment provider could not be reached for intent {IntentReference}.",
            intent.IntentReference);

        attempt.Status = PaymentAttemptStatus.TimedOut;
        attempt.GatewayMessage = exception.Message;
        attempt.DonorFacingMessage =
            "We could not reach the payment provider. Please try again in a few minutes.";

        intent.Status = DonationIntentStatus.Failed;
        intent.FailureReason = "The payment provider could not be reached.";

        await audit.WriteAnonymousAsync(
            AuditActionCodes.PaymentAttemptTimedOut,
            nameof(PaymentAttempt),
            attempt.Id,
            intent.TenantId,
            AuditResult.Failed,
            new { intent.IntentReference, attempt.AttemptNumber },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The provider answered, and the answer was no.</summary>
    private async Task RecordAttemptFailedAsync(
        DonationIntent intent,
        PaymentAttempt attempt,
        string? failureCode,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        attempt.Status = PaymentAttemptStatus.Failed;
        attempt.FailedAtUtc = clock.UtcNow;
        attempt.GatewayResultCode = failureCode;
        attempt.GatewayMessage = failureMessage;
        attempt.DonorFacingMessage =
            "We could not start this payment. Please try again or use another method.";

        intent.Status = DonationIntentStatus.Failed;
        intent.FailureReason = failureMessage;

        await audit.WriteAnonymousAsync(
            AuditActionCodes.PaymentAttemptFailed,
            nameof(PaymentAttempt),
            attempt.Id,
            intent.TenantId,
            AuditResult.Failed,
            new { intent.IntentReference, FailureCode = failureCode },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Puts back an attempt that never reached a provider.
    ///
    /// NOT A FAILURE, AND IT MATTERS THAT IT IS NOT RECORDED AS ONE. This runs when the provider
    /// simply does not offer in-page checkout, before any money was ever asked for. Marking the
    /// attempt Failed would put a phantom failed payment on the donation and in the queue; leaving
    /// the intent PaymentInProgress would make the payment-link call that follows look like a
    /// double submit and refuse it. So the attempt is cancelled and the counter wound back, and
    /// the donation is exactly where it was a moment ago.
    /// </summary>
    private async Task ReleaseAttemptAsync(
        DonationIntent intent, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        // ABANDONED IS THE HONEST TERMINAL STATE for an attempt that never reached a provider:
        // nothing further will happen to it, and unlike Initiated it will not be picked up as
        // live work. Failed would be a lie - no payment was ever asked for.
        attempt.Status = PaymentAttemptStatus.Abandoned;
        attempt.GatewayMessage = "The provider does not support an in-page checkout.";

        // BACK TO DRAFT, which is where the intent was a moment ago: captured, and not yet sent
        // to any gateway. The attempt counter is wound back with it so the payment link that
        // follows is attempt one rather than attempt two.
        intent.AttemptCount = Math.Max(0, intent.AttemptCount - 1);
        intent.Status = DonationIntentStatus.Draft;

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // =====================================================================================
    // Staff actions
    // =====================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelDonationIntentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await donations.GetIntentAsync(command.IntentId, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That donation was not found."));
        }

        if (intent.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // A PAID INTENT IS NOT CANCELLABLE. The money is in; the operation somebody wants is a
        // refund, which is a different thing with a different approval.
        if (intent.Status == DonationIntentStatus.Paid)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This donation has been paid. Raise a refund instead of cancelling it."));
        }

        if (intent.IsTerminal)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"This donation is already {intent.Status}."));
        }

        intent.Status = DonationIntentStatus.Cancelled;
        intent.CancellationReason = command.Request.Reason.Trim();
        intent.PaymentLinkUrl = null;

        await audit.WriteAsync(
            AuditActionCodes.IntentCancelled,
            nameof(DonationIntent),
            intent.Id,
            new { intent.IntentReference },
            command.Request.Reason,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(intent, "Donation cancelled.");
    }

    /// <summary>
    /// Sends the payment link again.
    ///
    /// It ISSUES A FRESH ONE rather than re-sending the old, because a link that has expired
    /// cannot be revived - and re-sending a dead link produces a donor who tries, fails and
    /// gives up.
    /// </summary>
    public async Task<Result<PaymentLinkResponse>> HandleAsync(
        ResendPaymentLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await donations.GetIntentAsync(command.IntentId, cancellationToken);

        if (intent is null)
        {
            return Result.Failure<PaymentLinkResponse>(Error.NotFound("That donation was not found."));
        }

        await audit.WriteAsync(
            AuditActionCodes.IntentPaymentLinkResent,
            nameof(DonationIntent),
            intent.Id,
            new { intent.IntentReference },
            cancellationToken: cancellationToken);

        return await HandleAsync(
            new CreatePaymentLinkCommand(
                intent.IntentReference, new CreatePaymentLinkRequest(command.ExpectedVersion)),
            cancellationToken);
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    /// <summary>
    /// The checks every payment-issuing path needs, in the order it needs them.
    ///
    /// EXPIRY IS CHECKED AGAINST THE INTENT, NOT THE LINK. A lapsed link is recoverable by
    /// issuing another; a lapsed intent is not, and telling a donor to try again on something
    /// that can never succeed is worse than telling them it has expired.
    /// </summary>
    private Result GuardPayable(DonationIntent intent, long expectedVersion)
    {
        if (intent.Version != expectedVersion)
        {
            return Result.Failure(Error.Concurrency());
        }

        return intent.Status switch
        {
            DonationIntentStatus.Paid => Result.Failure(Error.IntentAlreadyPaid()),
            DonationIntentStatus.Cancelled => Result.Failure(Error.IntentCancelled()),
            DonationIntentStatus.Expired => Result.Failure(Error.IntentExpired()),
            _ => Result.Success()
        };
    }

    /// <summary>
    /// Mints an unused intent reference.
    ///
    /// Checked ACROSS Organisations, because the reference is resolved globally by the public
    /// payment link - two intents sharing one would send a donor to somebody else's donation.
    /// </summary>
    private async Task<Result<string>> MintIntentReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReferenceAttempts; attempt++)
        {
            var candidate = references.NewIntentReference();

            if (!await donations.IntentReferenceExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }

            logger.LogWarning("Intent reference collision on attempt {Attempt}. Retrying.", attempt + 1);
        }

        // Five collisions against a random reference means the generator is broken, not that we
        // were unlucky.
        return Result.Failure<string>(Error.Dependency(
            "A unique donation reference could not be generated. Please try again shortly."));
    }

    private OutcomeResponse BuildOutcome(DonationIntent intent, string message) =>
        new(intent.Id,
            intent.Status.ToString(),
            intent.Version,
            message,
            PermittedActions(intent, clock.UtcNow));

    private IReadOnlyList<string> PermittedActions(DonationIntent intent, DateTimeOffset now) =>
        DonationMappingConfig.PermittedActionsFor(
            intent, currentUser.HasPermission, now, _settings.MaximumAttemptsBeforeSupport);
}
