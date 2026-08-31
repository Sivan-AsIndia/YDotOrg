using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Mappings;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Donations.Commands.ManageDonation;

/// <summary>Records a gift taken outside the gateway: a cheque, a bank transfer, cash at an event.</summary>
public sealed record RecordOfflineDonationCommand(RecordOfflineDonationRequest Request);

/// <summary>Marks a donation settled and reconciled against a bank statement.</summary>
public sealed record ReconcileDonationCommand(Guid DonationId, ReconcileDonationRequest Request);

/// <summary>
/// The two writes that act on a donation directly rather than through the gateway.
///
/// EVERYTHING ELSE THAT CREATES A DONATION GOES THROUGH A CAPTURED PAYMENT, which is why these
/// two are separated out and permissioned separately. An offline donation is the one path where
/// a person asserts that money arrived, with no gateway to corroborate it - so it is audited
/// with the operator's identity attached and the external reference they matched it against.
///
/// AN OFFLINE DONATION STILL GOES THROUGH AN INTENT. Creating a bare donation would leave it
/// with no attribution and no consent record, and a cheque handed in at an event is just as much
/// a gift to a campaign as a card payment. The intent is created and immediately marked Paid,
/// which also means the offline gift appears in exactly the same reports as every other.
/// </summary>
public sealed class DonationCommandHandler(
    IDonationRepository donations,
    ICampaignDirectory campaigns,
    IReferenceGenerator references,
    IAuditWriter audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<DonationCommandHandler> logger)
{
    /// <summary>How many times a reference collision is retried before giving up.</summary>
    private const int ReferenceAttempts = 5;

    // =====================================================================================
    // Offline donations
    // =====================================================================================

    /// <summary>
    /// Records a donation that arrived outside the gateway.
    ///
    /// THE WHOLE THING IS ONE TRANSACTION. It writes an intent, an attempt and a donation, and
    /// any of those committing without the others leaves the books describing something that did
    /// not happen - an intent with no gift, or a gift with no record of where it came from.
    ///
    /// THE ATTEMPT IS RECORDED TOO, with the external reference as its gateway reference. That
    /// looks like bookkeeping for its own sake and is not: the reconciliation screen matches a
    /// bank statement line against an attempt's reference, and an offline donation with no
    /// attempt would be invisible to the process that has to tick it off.
    ///
    /// THE DATE IS THE DATE THE MONEY ARRIVED, not today. A cheque banked in April for a gift
    /// received in March belongs to March's financial year, and getting that wrong puts the
    /// receipt in the wrong tax year - which the donor cannot then claim.
    /// </summary>
    public async Task<Result<DonationDetailResponse>> HandleAsync(
        RecordOfflineDonationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<DonationDetailResponse>(Error.TenantSelectionRequired());
        }

        if (!currentUser.HasPermission(PermissionCodes.DonationsRecordOffline))
        {
            return Result.Failure<DonationDetailResponse>(Error.Forbidden(
                "You do not have permission to record an offline donation."));
        }

        var tenantId = tenantContext.RequireTenantId();
        var now = clock.UtcNow;

        // A FUTURE-DATED GIFT IS REFUSED. Money that has not arrived yet is a pledge, which DON
        // records as a promise - recording it here would put income in the books before it
        // exists and let a receipt be issued for it.
        if (request.ReceivedAtUtc > now)
        {
            return Result.Failure<DonationDetailResponse>(Error.Validation(
                "An offline donation cannot be dated in the future."));
        }

        // A closed or unapproved campaign must not take money, whichever channel it came through.
        // The rule is the same for a cheque as for a card.
        if (request.CampaignId.HasValue)
        {
            var eligibility = await campaigns.GetDonationEligibilityAsync(
                tenantId, request.CampaignId.Value, cancellationToken);

            if (!eligibility.CanAcceptDonations)
            {
                return Result.Failure<DonationDetailResponse>(Error.Validation(
                    eligibility.Reason ?? "That campaign cannot accept donations."));
            }
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var intentReference = await MintAsync(
                references.NewIntentReference,
                candidate => donations.IntentReferenceExistsAsync(candidate, token),
                token);

            var donationReference = await MintAsync(
                references.NewDonationReference,
                candidate => donations.DonationReferenceExistsAsync(candidate, token),
                token);

            if (intentReference is null || donationReference is null)
            {
                return Result.Failure<DonationDetailResponse>(Error.Dependency(
                    "A unique reference could not be allocated. Please try again."));
            }

            var email = request.Email.Trim();
            var amount = MoneyValue.Create(request.Amount, request.CurrencyCode);

            var intent = new DonationIntent
            {
                TenantId = tenantId,
                BusinessUnitId = tenantContext.BusinessUnitId,
                IntentReference = intentReference,
                SourceType = DonationSourceType.OfflineEntry,
                CampaignId = request.CampaignId,
                DonorName = request.DonorName.Trim(),
                Email = email,
                NormalisedEmail = email.ToLowerInvariant(),
                Mobile = Clean(request.Mobile),
                TaxIdentifier = Clean(request.TaxIdentifier)?.ToUpperInvariant(),
                AddressLine1 = Clean(request.AddressLine1),
                PostalCode = Clean(request.PostalCode),

                // ITS OWN INSTANCE. See the note on the payment attempt below: one MoneyValue
                // object shared between entities is one tracked owned entity claimed by several
                // owners, and EF refuses it while saving.
                Amount = MoneyValue.Create(amount.Amount, amount.CurrencyCode),
                ConsentGiven = request.ConsentGiven,

                // Timestamped only where consent was actually given, so a null is unambiguous
                // rather than "given at the epoch".
                ConsentGivenAtUtc = request.ConsentGiven ? request.ReceivedAtUtc : null,

                // Paid from the outset. The money is already in hand; there is no link to follow
                // and nothing left to wait for.
                Status = DonationIntentStatus.Paid,

                AttemptCount = 1,
                LastAttemptAtUtc = request.ReceivedAtUtc
            };

            await donations.AddIntentAsync(intent, token);

            var attempt = new PaymentAttempt
            {
                TenantId = tenantId,
                BusinessUnitId = tenantContext.BusinessUnitId,
                DonationIntentId = intent.Id,
                AttemptNumber = 1,
                Status = PaymentAttemptStatus.Succeeded,

                // Named so nobody mistakes an offline entry for a gateway capture when reading
                // the attempt history.
                GatewayName = "Offline",

                // The cheque number or transfer reference. What reconciliation matches on.
                GatewayReference = Clean(request.ExternalReference),

                MethodType = request.MethodType,

                // TWO SEPARATE INSTANCES, not one object assigned twice. RequestedAmount and
                // CapturedAmount are owned entities, and EF tracks an owned entity BY REFERENCE:
                // handing it the same instance for both makes the two navigations one tracked
                // object with two identities, and it throws while saving - "the property
                // 'PaymentAttemptId' belongs to RequestedAmount#MoneyValue but is being used with
                // CapturedAmount#MoneyValue". Recording an offline gift failed with a 500 every
                // time, which took out one of the two ways a donation can be recorded at all.
                //
                // The values are equal here because an offline gift is captured in full the
                // moment it is recorded; equal is not the same as identical.
                RequestedAmount = MoneyValue.Create(amount.Amount, amount.CurrencyCode),
                CapturedAmount = MoneyValue.Create(amount.Amount, amount.CurrencyCode),
                InitiatedAtUtc = request.ReceivedAtUtc,
                CapturedAtUtc = request.ReceivedAtUtc,
                GatewayMessage = Clean(request.Notes),
                DonorFacingMessage = null
            };

            await donations.AddAttemptAsync(attempt, token);

            var donation = new Donation
            {
                TenantId = tenantId,
                BusinessUnitId = tenantContext.BusinessUnitId,
                DonationReference = donationReference,
                DonationIntentId = intent.Id,
                PaymentAttemptId = attempt.Id,
                CampaignId = request.CampaignId,
                Amount = MoneyValue.Create(amount.Amount, amount.CurrencyCode),

                // Zero in the SAME currency. A refunded amount with no currency, or one in a
                // different currency from the gift, makes the refundable balance uncomputable.
                RefundedAmount = MoneyValue.Zero(request.CurrencyCode),

                // The donor as they were AT THE MOMENT OF THE GIFT. A receipt issued later has to
                // reproduce this, not whatever the donor record says by then.
                DonorName = intent.DonorName,
                DonorEmail = intent.Email,
                DonorMobile = intent.Mobile,
                DonorTaxIdentifier = intent.TaxIdentifier,
                DonorAddress = ComposeAddress(request.AddressLine1, request.PostalCode),

                Status = DonationStatus.Recorded,
                DonatedAtUtc = request.ReceivedAtUtc,
                MethodType = request.MethodType,
                GatewayReference = Clean(request.ExternalReference),

                // AN OFFLINE GIFT IS ALREADY IN THE CHARITY'S HANDS - there is no settlement
                // window and no provider holding it - so it is settled on arrival. What it is
                // NOT is reconciled: somebody still has to tick it against the bank statement.
                SettlementStatus = SettlementStatus.Settled,
                SettledAtUtc = request.ReceivedAtUtc,
                ReconciliationStatus = ReconciliationStatus.Unreconciled,

                SourceType = DonationSourceType.OfflineEntry
            };

            await donations.AddDonationAsync(donation, token);

            await audit.WriteAsync(
                AuditActionCodes.DonationOfflineRecorded,
                nameof(Donation),
                donation.Id,
                new
                {
                    donation.DonationReference,
                    Amount = amount.ToString(),
                    Method = request.MethodType.ToString(),
                    request.ExternalReference,
                    ReceivedAt = request.ReceivedAtUtc
                },
                request.Notes,
                token);

            await unitOfWork.SaveChangesAsync(token);

            logger.LogInformation(
                "Offline donation {DonationReference} recorded for organisation {TenantId} by {UserId}.",
                donation.DonationReference,
                tenantId,
                currentUser.UserId);

            return Result.Success(await BuildDetailAsync(donation, intent, tenantId, token));
        }, cancellationToken);
    }

    // =====================================================================================
    // Reconciliation
    // =====================================================================================

    /// <summary>
    /// Marks a donation reconciled against a bank statement.
    ///
    /// RECONCILIATION IS AN ASSERTION BY A PERSON, which is why it carries a note and an audit
    /// row naming them. "Matched" means somebody looked at a statement line and this donation and
    /// said they are the same money; nothing automatic can make that claim, and an auditor asking
    /// who decided is entitled to an answer.
    ///
    /// MATCHING ALSO SETTLES THE DONATION where it was not already settled - the money is
    /// demonstrably in the bank, which is what settled means. A discrepancy does the opposite:
    /// it leaves settlement alone, because "these figures do not agree" is not evidence about
    /// where the money is.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReconcileDonationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (!currentUser.HasPermission(PermissionCodes.DonationsReconcile))
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden(
                "You do not have permission to reconcile donations."));
        }

        var donation = await donations.GetDonationAsync(command.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That donation was not found."));
        }

        if (donation.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // A DISCREPANCY MUST BE EXPLAINED. Marking a figure as disagreeing with the bank and
        // saying nothing about how leaves the next person with a flag and no lead to follow.
        if (request.Status == ReconciliationStatus.Discrepancy
            && string.IsNullOrWhiteSpace(request.Note))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "A discrepancy has to say what does not agree."));
        }

        // So must a manual resolution - it is the record of a judgement call, and a judgement
        // call with no reasoning is indistinguishable from a mistake.
        if (request.Status == ReconciliationStatus.ManuallyResolved
            && string.IsNullOrWhiteSpace(request.Note))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "A manual resolution has to record why it was resolved this way."));
        }

        var now = clock.UtcNow;

        donation.ReconciliationStatus = request.Status;
        donation.ReconciliationNote = Clean(request.Note);
        donation.SettlementBatchReference =
            Clean(request.SettlementBatchReference) ?? donation.SettlementBatchReference;

        // Unreconciled clears the date rather than keeping a stale one: putting a record back
        // into the queue and leaving "reconciled on the 4th" beside it is a contradiction.
        donation.ReconciledAtUtc =
            request.Status == ReconciliationStatus.Unreconciled ? null : now;

        if (request.Status == ReconciliationStatus.Matched)
        {
            donation.SettlementStatus = SettlementStatus.Settled;
            donation.SettledAtUtc = request.SettledAtUtc ?? donation.SettledAtUtc ?? now;

            // Recorded becomes Settled. The later states - refunded, charged back, voided - are
            // deliberately left alone: reconciling a refunded donation confirms the ORIGINAL
            // money arrived and says nothing about the refund that followed.
            if (donation.Status == DonationStatus.Recorded)
            {
                donation.Status = DonationStatus.Settled;
            }
        }

        await audit.WriteAsync(
            AuditActionCodes.DonationReconciled,
            nameof(Donation),
            donation.Id,
            new
            {
                donation.DonationReference,
                Status = request.Status.ToString(),
                request.SettlementBatchReference,
                request.SettledAtUtc
            },
            request.Note,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Donation {DonationReference} marked {Status} by {UserId}.",
            donation.DonationReference,
            request.Status,
            currentUser.UserId);

        return Result.Success(new OutcomeResponse(
            donation.Id,
            donation.ReconciliationStatus.ToString(),
            donation.Version,
            $"Donation marked {donation.ReconciliationStatus}.",
            DonationMappingConfig.PermittedActionsFor(donation, currentUser.HasPermission)));
    }

    // =====================================================================================
    // Internals
    // =====================================================================================

    /// <summary>
    /// The detail response for a donation that was just created.
    ///
    /// Built here rather than through the read service because the read service goes through the
    /// query filter, and inside an uncommitted transaction the row it would look for is one only
    /// this connection can see.
    /// </summary>
    private async Task<DonationDetailResponse> BuildDetailAsync(
        Donation donation, DonationIntent intent, Guid tenantId, CancellationToken cancellationToken)
    {
        var campaignName = donation.CampaignId.HasValue
            ? await campaigns.GetCampaignNameAsync(tenantId, donation.CampaignId.Value, cancellationToken)
            : null;

        return new DonationDetailResponse(
            donation.Id,
            donation.TenantId,
            donation.DonationReference,
            donation.DonationIntentId,
            intent.IntentReference,
            donation.PaymentAttemptId,
            donation.DonorId,
            donation.CampaignId,
            campaignName,
            donation.Amount.ToResponse(),
            donation.GatewayFee.ToResponseOrNull(),
            donation.NetAmount.ToResponseOrNull(),
            donation.RefundedAmount.ToResponse(),
            donation.RefundableAmount.ToResponse(),
            donation.DonorName,

            // NOT MASKED. The operator just typed these details in; masking them back would be
            // theatre, and they hold the offline-recording permission in any case.
            donation.DonorEmail,
            donation.DonorMobile,
            donation.DonorTaxIdentifier,
            donation.DonorAddress,

            donation.Status,
            PaymentMappingConfig.Describe(donation.Status),
            donation.DonatedAtUtc,
            donation.MethodType,
            donation.GatewayReference,
            donation.SettlementStatus,
            donation.SettledAtUtc,
            donation.SettlementBatchReference,
            donation.ReconciliationStatus,
            donation.ReconciledAtUtc,
            donation.ReconciliationNote,
            donation.SourceType,
            PaymentMappingConfig.Describe(donation.SourceType),
            donation.TrackingAssetId,
            donation.LeadId,
            donation.IsReceiptable,
            [],
            [],
            [],
            donation.CreatedAtUtc,
            donation.CreatedByUserId,
            donation.UpdatedAtUtc,
            donation.UpdatedByUserId,
            donation.Version,
            DonationMappingConfig.PermittedActionsFor(donation, currentUser.HasPermission));
    }

    /// <summary>
    /// Generates a reference and checks it is free, a few times.
    ///
    /// A COLLISION IS ASTRONOMICALLY UNLIKELY - twelve characters from an alphabet of 31 - but
    /// the consequence of one is a unique-index violation surfacing to a person as an error on an
    /// operation that had nothing wrong with it. Checking is cheap; explaining that is not.
    /// </summary>
    private static async Task<string?> MintAsync(
        Func<string> generate,
        Func<string, Task<bool>> exists,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReferenceAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = generate();

            if (!await exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ComposeAddress(string? addressLine, string? postalCode)
    {
        var parts = new[] { Clean(addressLine), Clean(postalCode) }
            .Where(part => part is not null);

        var joined = string.Join(", ", parts);

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
