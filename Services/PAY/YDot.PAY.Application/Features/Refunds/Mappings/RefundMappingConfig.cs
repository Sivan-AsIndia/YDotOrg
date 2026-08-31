using System.Globalization;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Refunds.Mappings;

/// <summary>Manual mapping for the Refunds and Chargebacks slice.</summary>
public static class RefundMappingConfig
{
    // =====================================================================================
    // Refunds
    // =====================================================================================

    /// <summary>A refund as a donation detail screen shows it inline.</summary>
    public static RefundCaseSummaryResponse ToSummaryResponse(this RefundCase refundCase)
    {
        ArgumentNullException.ThrowIfNull(refundCase);

        return new RefundCaseSummaryResponse(
            refundCase.Id,
            refundCase.CaseReference,
            refundCase.Status,
            PaymentMappingConfig.Describe(refundCase.Status),
            refundCase.Amount.ToResponse(),
            refundCase.Reason,
            refundCase.RequestedAtUtc);
    }

    /// <summary>One row of the refund register.</summary>
    public static RefundCaseListItemResponse ToListItemResponse(
        this RefundCase refundCase, Donation donation, bool canSeeSensitiveDonor)
    {
        ArgumentNullException.ThrowIfNull(refundCase);
        ArgumentNullException.ThrowIfNull(donation);

        return new RefundCaseListItemResponse(
            refundCase.Id,
            refundCase.CaseReference,
            refundCase.DonationId,
            donation.DonationReference,
            donation.DonorName,
            refundCase.Amount.ToResponse(),
            donation.Amount.ToResponse(),
            refundCase.Status,
            PaymentMappingConfig.Describe(refundCase.Status),
            refundCase.Reason,
            PaymentMappingConfig.Describe(refundCase.Reason),
            refundCase.RequestedByUserId,
            refundCase.RequestedAtUtc,
            refundCase.DecidedByUserId,
            refundCase.DecidedAtUtc,
            refundCase.ReceiptCorrected,
            refundCase.Version);
    }

    /// <summary>The full refund case.</summary>
    public static RefundCaseDetailResponse ToDetailResponse(
        this RefundCase refundCase,
        Donation donation,
        bool canSeeSensitiveDonor,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(refundCase);
        ArgumentNullException.ThrowIfNull(donation);

        return new RefundCaseDetailResponse(
            refundCase.Id,
            refundCase.TenantId,
            refundCase.CaseReference,
            refundCase.DonationId,
            donation.DonationReference,
            donation.DonorName,
            PaymentMappingConfig.MaskEmail(donation.DonorEmail, canSeeSensitiveDonor),
            refundCase.Amount.ToResponse(),
            donation.Amount.ToResponse(),
            donation.RefundableAmount.ToResponse(),
            refundCase.Status,
            PaymentMappingConfig.Describe(refundCase.Status),
            refundCase.Reason,
            PaymentMappingConfig.Describe(refundCase.Reason),
            refundCase.ReasonDetail,
            refundCase.RequestedByUserId,
            refundCase.RequestedAtUtc,
            refundCase.DecidedByUserId,
            refundCase.DecidedAtUtc,
            refundCase.DecisionNote,
            refundCase.RejectionReason,
            refundCase.GatewayRefundReference,
            refundCase.ProcessedAtUtc,
            refundCase.CompletedAtUtc,
            refundCase.GatewayFailureReason,
            refundCase.ReceiptCorrected,
            refundCase.CreatedAtUtc,
            refundCase.CreatedByUserId,
            refundCase.UpdatedAtUtc,
            refundCase.UpdatedByUserId,
            refundCase.Version,
            permittedActions);
    }

    /// <summary>One line of the refund export.</summary>
    public static RefundExportRow ToExportRow(this RefundCase refundCase, Donation donation)
    {
        ArgumentNullException.ThrowIfNull(refundCase);
        ArgumentNullException.ThrowIfNull(donation);

        return new RefundExportRow(
            refundCase.CaseReference,
            donation.DonationReference,
            donation.DonorName,
            refundCase.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            refundCase.Amount.CurrencyCode,
            refundCase.Status.ToString(),
            refundCase.Reason.ToString(),
            refundCase.ReasonDetail,
            refundCase.RequestedAtUtc.ToString("u", CultureInfo.InvariantCulture),
            refundCase.DecidedAtUtc?.ToString("u", CultureInfo.InvariantCulture),
            refundCase.CompletedAtUtc?.ToString("u", CultureInfo.InvariantCulture),
            refundCase.ReceiptCorrected ? "Yes" : "No");
    }

    /// <summary>
    /// What may be done to a refund case next.
    ///
    /// APPROVE AND REJECT ARE ABSENT FOR THE PERSON WHO RAISED IT, whatever permissions they
    /// hold. Deciding that here as well as in the handler is what stops the screen drawing a
    /// button that will answer 409.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        RefundCase refundCase, Guid callerUserId, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(refundCase);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.RefundsView))
        {
            actions.Add("View");
        }

        if (hasPermission(PermissionCodes.RefundsExport))
        {
            actions.Add("Export");
        }

        if (refundCase.Status != RefundStatus.Requested)
        {
            return actions;
        }

        var isIndependent = refundCase.CanBeDecidedBy(callerUserId);

        if (isIndependent && hasPermission(PermissionCodes.RefundsApprove))
        {
            actions.Add("Approve");
        }

        if (isIndependent && hasPermission(PermissionCodes.RefundsReject))
        {
            actions.Add("Reject");
        }

        return actions;
    }

    // =====================================================================================
    // Chargebacks
    // =====================================================================================

    /// <summary>A chargeback as a donation detail screen shows it inline.</summary>
    public static ChargebackCaseSummaryResponse ToSummaryResponse(
        this ChargebackCase chargeback, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(chargeback);

        return new ChargebackCaseSummaryResponse(
            chargeback.Id,
            chargeback.CaseReference,
            chargeback.Status,
            PaymentMappingConfig.Describe(chargeback.Status),
            chargeback.DisputedAmount.ToResponse(),
            chargeback.OpenedAtUtc,
            chargeback.EvidenceDueAtUtc,
            chargeback.IsOverdueAt(now));
    }

    /// <summary>One row of the chargeback register.</summary>
    public static ChargebackCaseListItemResponse ToListItemResponse(
        this ChargebackCase chargeback, Donation donation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(chargeback);
        ArgumentNullException.ThrowIfNull(donation);

        return new ChargebackCaseListItemResponse(
            chargeback.Id,
            chargeback.CaseReference,
            chargeback.DonationId,
            donation.DonationReference,
            donation.DonorName,
            chargeback.DisputedAmount.ToResponse(),
            chargeback.ChargebackFee.ToResponseOrNull(),
            chargeback.Status,
            PaymentMappingConfig.Describe(chargeback.Status),
            chargeback.ReasonCode,
            chargeback.ReasonDescription,
            chargeback.OpenedAtUtc,
            chargeback.EvidenceDueAtUtc,
            DaysUntilDue(chargeback, now),
            chargeback.IsOverdueAt(now),
            chargeback.AssignedToUserId,
            chargeback.Version);
    }

    /// <summary>The full chargeback case.</summary>
    public static ChargebackCaseDetailResponse ToDetailResponse(
        this ChargebackCase chargeback,
        Donation donation,
        DateTimeOffset now,
        bool canSeeSensitiveDonor,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(chargeback);
        ArgumentNullException.ThrowIfNull(donation);

        return new ChargebackCaseDetailResponse(
            chargeback.Id,
            chargeback.TenantId,
            chargeback.CaseReference,
            chargeback.DonationId,
            donation.DonationReference,
            donation.DonorName,
            PaymentMappingConfig.MaskEmail(donation.DonorEmail, canSeeSensitiveDonor),
            chargeback.DisputedAmount.ToResponse(),
            chargeback.ChargebackFee.ToResponseOrNull(),
            chargeback.Status,
            PaymentMappingConfig.Describe(chargeback.Status),
            chargeback.GatewayDisputeReference,
            chargeback.ReasonCode,
            chargeback.ReasonDescription,
            chargeback.OpenedAtUtc,
            chargeback.EvidenceDueAtUtc,
            DaysUntilDue(chargeback, now),
            chargeback.IsOverdueAt(now),
            chargeback.EvidenceSubmittedAtUtc,
            chargeback.EvidenceSubmittedByUserId,
            chargeback.EvidenceSummary,
            SplitUrls(chargeback.EvidenceDocumentUrls),
            chargeback.ResolvedAtUtc,
            chargeback.ResolutionNote,
            chargeback.AssignedToUserId,
            chargeback.CreatedAtUtc,
            chargeback.CreatedByUserId,
            chargeback.UpdatedAtUtc,
            chargeback.UpdatedByUserId,
            chargeback.Version,
            permittedActions);
    }

    /// <summary>What may be done to a chargeback case next.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        ChargebackCase chargeback, DateTimeOffset now, Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(chargeback);
        ArgumentNullException.ThrowIfNull(hasPermission);

        var actions = new List<string>();

        if (hasPermission(PermissionCodes.ChargebacksView))
        {
            actions.Add("View");
        }

        if (!chargeback.IsOpen)
        {
            return actions;
        }

        if (hasPermission(PermissionCodes.ChargebacksAssign))
        {
            actions.Add("Assign");
        }

        // Evidence is offered only while the deadline is still ahead. Offering it afterwards
        // would let somebody believe they had contested a case the bank will never look at.
        var deadlinePassed = chargeback.EvidenceDueAtUtc.HasValue
                             && chargeback.EvidenceDueAtUtc.Value < now;

        if (!deadlinePassed
            && chargeback.EvidenceSubmittedAtUtc is null
            && hasPermission(PermissionCodes.ChargebacksSubmitEvidence))
        {
            actions.Add("SubmitEvidence");
        }

        if (hasPermission(PermissionCodes.ChargebacksResolve))
        {
            actions.Add("Resolve");
        }

        return actions;
    }

    /// <summary>
    /// Days left to submit evidence, negative once the deadline has passed.
    ///
    /// Computed on the SERVER so every client shows the same number and the queue can be sorted
    /// by urgency without each one repeating the arithmetic.
    /// </summary>
    private static int? DaysUntilDue(ChargebackCase chargeback, DateTimeOffset now) =>
        chargeback.EvidenceDueAtUtc.HasValue
            ? (int)Math.Ceiling((chargeback.EvidenceDueAtUtc.Value - now).TotalDays)
            : null;

    private static IReadOnlyList<string> SplitUrls(string? commaSeparated) =>
        string.IsNullOrWhiteSpace(commaSeparated)
            ? []
            : [.. commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
