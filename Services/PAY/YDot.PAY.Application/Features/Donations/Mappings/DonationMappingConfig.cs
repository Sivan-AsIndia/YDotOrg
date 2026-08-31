using System.Globalization;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Donations.Mappings;

/// <summary>Manual mapping for the Donations slice: intents, attempts and donations.</summary>
public static class DonationMappingConfig
{
    /// <summary>
    /// Builds a new donation intent from a public request.
    ///
    /// <paramref name="tenantId"/> AND <paramref name="businessUnitId"/> ARE PASSED IN, never
    /// taken from the request. They come from the tracking reference or the campaign the donor
    /// followed; a caller who could name them could create donations against any charity on the
    /// platform.
    ///
    /// The e-mail is stored twice on purpose - see <see cref="DonationIntent.NormalisedEmail"/>.
    /// </summary>
    public static DonationIntent ToEntity(
        this CreateDonationIntentRequest request,
        Guid tenantId,
        Guid businessUnitId,
        string intentReference,
        Guid? trackingAssetId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();

        return new DonationIntent
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            IntentReference = intentReference,
            SourceType = request.SourceType,
            CampaignId = request.CampaignId,
            TrackingAssetId = trackingAssetId,
            TrackingReference = Clean(request.TrackingReference),
            LeadId = request.LeadReference,
            DonorName = request.DonorName.Trim(),
            Email = email,

            // Lower-cased and trimmed. The column the section 26 existing-donor lookup uses.
            NormalisedEmail = email.ToLowerInvariant(),

            Mobile = Clean(request.Mobile),
            TaxIdentifier = Clean(request.TaxIdentifier)?.ToUpperInvariant(),
            AddressLine1 = Clean(request.AddressLine1),
            AddressLine2 = Clean(request.AddressLine2),
            CountryId = request.CountryId,
            StateId = request.StateId,
            CityId = request.CityId,
            PostalCode = Clean(request.PostalCode),
            Amount = MoneyValue.Create(request.Amount, request.CurrencyCode),
            ConsentGiven = request.ConsentGiven,
            ConsentVersion = Clean(request.ConsentVersion),

            // Timestamped only where consent was actually given, so a null is unambiguous rather
            // than "given at the epoch".
            ConsentGivenAtUtc = request.ConsentGiven ? now : null,

            AllowPublicRecognition = request.AllowPublicRecognition,
            PublicRecognitionName = Clean(request.PublicRecognitionName),
            Status = DonationIntentStatus.Draft
        };
    }

    /// <summary>The response the donor's browser gets after creating an intent.</summary>
    public static DonationIntentResponse ToResponse(
        this DonationIntent intent, string? campaignName, IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return new DonationIntentResponse(
            intent.Id,
            intent.IntentReference,
            intent.Status,
            PaymentMappingConfig.Describe(intent.Status),
            intent.Amount.ToResponse(),
            intent.DonorName,

            // NOT masked here. This response goes back to the donor who just typed the address,
            // so hiding it from them would be theatre rather than protection.
            intent.Email,

            intent.Mobile,
            intent.CampaignId,
            campaignName,
            intent.SourceType,
            intent.TrackingReference,
            intent.ExistingDonorMatched,
            intent.PaymentLinkUrl,
            intent.PaymentLinkExpiresAtUtc,
            intent.AttemptCount,
            intent.CreatedAtUtc,
            intent.Version,
            permittedActions);
    }

    /// <summary>One row of the intent register. Staff-facing, so the donor details are masked.</summary>
    public static DonationIntentListItemResponse ToListItemResponse(
        this DonationIntent intent, string? campaignName, bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return new DonationIntentListItemResponse(
            intent.Id,
            intent.IntentReference,
            intent.DonorName,
            PaymentMappingConfig.MaskEmail(intent.Email, canSeeSensitiveDonor),
            intent.Amount.ToResponse(),
            intent.Status,
            PaymentMappingConfig.Describe(intent.Status),
            intent.SourceType,
            intent.CampaignId,
            campaignName,
            intent.AttemptCount,
            intent.LastAttemptAtUtc,
            intent.ExistingDonorMatched,
            intent.CreatedAtUtc,
            intent.Version);
    }

    /// <summary>One attempt, as the support timeline shows it.</summary>
    public static PaymentAttemptResponse ToResponse(this PaymentAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new PaymentAttemptResponse(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Status,
            PaymentMappingConfig.Describe(attempt.Status),
            attempt.GatewayName,
            attempt.GatewayReference,
            attempt.MethodType,
            attempt.MaskedInstrument,
            attempt.RequestedAmount.ToResponse(),
            attempt.CapturedAmount.ToResponseOrNull(),
            attempt.InitiatedAtUtc,
            attempt.CapturedAtUtc,
            attempt.FailedAtUtc,
            attempt.GatewayResultCode,
            attempt.DonorFacingMessage,
            attempt.NeedsVerification);
    }

    /// <summary>A donation as the intent detail screen shows it inline.</summary>
    public static DonationSummaryResponse ToSummaryResponse(
        this Donation donation, string? receiptNumber)
    {
        ArgumentNullException.ThrowIfNull(donation);

        return new DonationSummaryResponse(
            donation.Id,
            donation.DonationReference,
            donation.Amount.ToResponse(),
            donation.Status,
            PaymentMappingConfig.Describe(donation.Status),
            donation.DonatedAtUtc,
            donation.HasIssuedReceipt,
            receiptNumber);
    }

    /// <summary>One row of the donation register.</summary>
    public static DonationListItemResponse ToListItemResponse(
        this Donation donation,
        string? campaignName,
        string? receiptNumber,
        bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(donation);

        return new DonationListItemResponse(
            donation.Id,
            donation.DonationReference,
            donation.DonorName,
            PaymentMappingConfig.MaskEmail(donation.DonorEmail, canSeeSensitiveDonor),
            donation.Amount.ToResponse(),
            donation.NetAmount.ToResponseOrNull(),
            donation.Status,
            PaymentMappingConfig.Describe(donation.Status),
            donation.SettlementStatus,
            donation.ReconciliationStatus,
            donation.DonatedAtUtc,
            donation.MethodType,
            donation.CampaignId,
            campaignName,
            donation.SourceType,
            donation.HasIssuedReceipt,
            receiptNumber,
            donation.HasOpenCase,
            donation.Version);
    }

    /// <summary>One line of the donation export.</summary>
    public static DonationExportRow ToExportRow(
        this Donation donation,
        string intentReference,
        string? campaignName,
        string? receiptNumber,
        bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(donation);

        return new DonationExportRow(
            donation.DonationReference,
            intentReference,
            donation.DonorName,

            // MASKED IN THE EXPORT TOO. A CSV outlives the session that produced it and travels
            // by e-mail; if anything it needs the masking more than the screen does.
            PaymentMappingConfig.MaskEmail(donation.DonorEmail, canSeeSensitiveDonor),

            donation.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            donation.Amount.CurrencyCode,
            donation.NetAmount?.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            donation.Status.ToString(),
            donation.SettlementStatus.ToString(),
            donation.ReconciliationStatus.ToString(),
            donation.DonatedAtUtc.ToString("u", CultureInfo.InvariantCulture),
            donation.MethodType?.ToString(),
            campaignName,
            donation.SourceType.ToString(),
            receiptNumber,
            donation.RefundedAmount.Amount.ToString(CultureInfo.InvariantCulture));
    }

    // =====================================================================================
    // Permitted actions
    // =====================================================================================

    /// <summary>
    /// What may be done to an intent next.
    ///
    /// USED FOR BOTH THE DONOR AND STAFF, with <paramref name="hasPermission"/> answering false
    /// for everything on the public path - so a donor gets Pay and Retry from the state alone,
    /// and staff additionally get Cancel and Resend from their permissions.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        DonationIntent intent,
        Func<string, bool> hasPermission,
        DateTimeOffset now,
        int maximumAttemptsBeforeSupport)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string> { "View" };

        if (intent.Status == DonationIntentStatus.Paid)
        {
            actions.Add("ViewDonation");
            return actions;
        }

        if (intent.IsTerminal)
        {
            return actions;
        }

        // The link has lapsed but the intent has not: a fresh link can still be issued.
        var linkExpired = intent.IsLinkExpiredAt(now);

        if (intent.IsPayable && !linkExpired && !string.IsNullOrWhiteSpace(intent.PaymentLinkUrl))
        {
            actions.Add("Pay");
        }

        if (intent.IsPayable)
        {
            // Past the attempt cap the donor is not offered another identical button - section 23
            // routes them to Payment Support and Safe Retry instead.
            if (intent.AttemptCount < maximumAttemptsBeforeSupport)
            {
                actions.Add("Retry");
            }
            else
            {
                actions.Add("ContactSupport");
            }
        }

        if (hasPermission(PermissionCodes.IntentsResendLink) && intent.IsPayable)
        {
            actions.Add("ResendLink");
        }

        if (hasPermission(PermissionCodes.IntentsCancel) && !intent.IsTerminal)
        {
            actions.Add("Cancel");
        }

        if (hasPermission(PermissionCodes.PaymentsSafeRetry)
            && intent.Status is DonationIntentStatus.Failed or DonationIntentStatus.PaymentInProgress)
        {
            actions.Add("SafeRetry");
        }

        return actions;
    }

    /// <summary>What may be done to a donation next.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        Donation donation, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(donation);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.DonationsView))
        {
            actions.Add("View");
        }

        if (hasPermission(PermissionCodes.DonationsExport))
        {
            actions.Add("Export");
        }

        // A receipt may be issued once, and only while the donation is receiptable. A second one
        // is a CORRECTION of the first, which is a different action with its own permission.
        if (donation.IsReceiptable
            && !donation.HasIssuedReceipt
            && hasPermission(PermissionCodes.ReceiptsIssue))
        {
            actions.Add("IssueReceipt");
        }

        if (donation.HasIssuedReceipt && hasPermission(PermissionCodes.ReceiptsResend))
        {
            actions.Add("ResendReceipt");
        }

        // A refund needs something left to give back and no case already running.
        if (!donation.RefundableAmount.IsZero
            && !donation.HasOpenCase
            && donation.Status is not (DonationStatus.Voided or DonationStatus.ChargedBack)
            && hasPermission(PermissionCodes.RefundsRequest))
        {
            actions.Add("RequestRefund");
        }

        if (donation.ReconciliationStatus != ReconciliationStatus.Matched
            && hasPermission(PermissionCodes.DonationsReconcile))
        {
            actions.Add("Reconcile");
        }

        return actions;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
