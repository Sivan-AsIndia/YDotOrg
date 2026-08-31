using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Domain.Entities;

/// <summary>
/// A payment the donor's bank reversed without asking.
///
/// NOT A REFUND, AND NOT MODELLED AS ONE. Three things differ and all three matter: the
/// organisation did not choose it, there is a DEADLINE to respond, and losing usually costs a
/// fee on top of the money. It has its own evidence, its own clock and its own outcome.
/// </summary>
public sealed class ChargebackCase : TenantEntity
{
    public string CaseReference { get; set; } = string.Empty;

    public Guid DonationId { get; set; }

    public Donation Donation { get; set; } = default!;

    public ChargebackStatus Status { get; set; } = ChargebackStatus.Opened;

    /// <summary>The gateway's own dispute id.</summary>
    public string? GatewayDisputeReference { get; set; }

    /// <summary>The bank's reason code, kept verbatim - it decides what evidence is worth sending.</summary>
    public string? ReasonCode { get; set; }

    public string? ReasonDescription { get; set; }

    /// <summary>How much the bank pulled back.</summary>
    public MoneyValue DisputedAmount { get; set; } = default!;

    /// <summary>The penalty the gateway charges for handling it. Payable even if the case is won.</summary>
    public MoneyValue? ChargebackFee { get; set; }

    public DateTimeOffset OpenedAtUtc { get; set; }

    /// <summary>
    /// When evidence must be in by.
    ///
    /// THE FIELD THE WHOLE CASE TURNS ON. Miss it and the case is lost by default, whatever the
    /// merits - so it is a stored column the queue sorts by rather than something a person is
    /// expected to remember.
    /// </summary>
    public DateTimeOffset? EvidenceDueAtUtc { get; set; }

    public DateTimeOffset? EvidenceSubmittedAtUtc { get; set; }

    public Guid? EvidenceSubmittedByUserId { get; set; }

    /// <summary>What was argued and what was attached.</summary>
    public string? EvidenceSummary { get; set; }

    public string? EvidenceDocumentUrls { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public string? ResolutionNote { get; set; }

    /// <summary>Who is working it. A case with no owner is a case nobody is working.</summary>
    public Guid? AssignedToUserId { get; set; }

    public bool IsOpen => Status is ChargebackStatus.Opened
        or ChargebackStatus.EvidenceRequired
        or ChargebackStatus.UnderReview;

    /// <summary>Whether the deadline has passed with no evidence submitted.</summary>
    public bool IsOverdueAt(DateTimeOffset moment) =>
        IsOpen
        && EvidenceDueAtUtc.HasValue
        && EvidenceDueAtUtc.Value < moment
        && EvidenceSubmittedAtUtc is null;
}
