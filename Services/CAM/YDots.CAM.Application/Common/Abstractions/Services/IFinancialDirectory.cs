namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>What has actually come in against one campaign, and how it breaks down by source.</summary>
public sealed record CampaignIncome
{
    public Guid CampaignId { get; init; }

    /// <summary>Confirmed donations only. A pending payment is not income.</summary>
    public decimal ConfirmedAmount { get; init; }

    public int DonationCount { get; init; }

    /// <summary>Distinct donors who gave to the campaign, for an average-gift figure.</summary>
    public int DonorCount { get; init; }

    /// <summary>Refunded and charged-back amounts, already deducted from the confirmed total.</summary>
    public decimal RefundedAmount { get; init; }
}

/// <summary>One attributed donation, as the attribution explorer reads it.</summary>
public sealed record AttributedDonation
{
    public Guid DonationId { get; init; }

    public string Reference { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; init; }

    public decimal Amount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public Guid? CampaignId { get; init; }

    public string CampaignName { get; init; } = string.Empty;

    public Guid? TrackingAssetId { get; init; }

    /// <summary>The reference the donor's link or QR code carried. Empty on a direct gift.</summary>
    public string TrackingReference { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string MediumName { get; init; } = string.Empty;

    public string DonorName { get; init; } = string.Empty;

    public Guid? DonorId { get; init; }

    /// <summary>
    /// Whether the gift could be traced to a tracking asset at all.
    ///
    /// THE MOST IMPORTANT FIELD ON THIS RECORD. An unattributed donation is not a failure - many
    /// people simply type the address in - but a report that quietly folded them into a campaign's
    /// figures would credit that campaign with money it did not raise.
    /// </summary>
    public bool IsAttributed { get; init; }

    /// <summary>True while somebody has asked for the attribution to be looked at again.</summary>
    public bool HasOpenCorrectionRequest { get; init; }
}

/// <summary>
/// Money, read from the payments and reference tables over the shared database.
///
/// EVERYTHING HERE IS READ-ONLY, which is what makes a seam over a shared database acceptable.
/// CAM never writes a donation, a receipt or a currency; it needs to show what a campaign has
/// raised against what it planned to raise, and to let somebody see which of its tracking assets
/// produced the money. If payments ever move to their own database, only the implementation of
/// this interface changes.
///
/// CAM MUST NOT REPRODUCE THE PAYMENT RULES. What counts as confirmed income, what a refund does
/// to a total, when a chargeback is deducted - all of that belongs to PAY, and a second
/// implementation of it here would drift from the first the week after it was written. These
/// queries read what PAY has already decided.
/// </summary>
public interface IFinancialDirectory
{
    /// <summary>
    /// Currency codes by id.
    ///
    /// TAKES A SET, NOT AN ID. A plan register showing twenty rows references perhaps two
    /// currencies; asking per row would be twenty queries to render one screen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCurrencyCodesAsync(
        IReadOnlyCollection<Guid> currencyIds, CancellationToken cancellationToken);

    /// <summary>What one campaign has actually raised.</summary>
    Task<CampaignIncome> GetCampaignIncomeAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken);

    /// <summary>What several campaigns have raised, for a register that shows progress per row.</summary>
    Task<IReadOnlyDictionary<Guid, CampaignIncome>> GetCampaignIncomeAsync(
        Guid tenantId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken);

    /// <summary>
    /// The donations attributed to an organisation, newest first.
    ///
    /// PAGED AT THE DATABASE, because an organisation that has been running for a year has more
    /// donations than a browser should ever be handed - and the screen that reads this is the one
    /// people use to investigate a single gift, not to download the lot.
    /// </summary>
    Task<(IReadOnlyList<AttributedDonation> Items, int TotalCount)> SearchAttributedDonationsAsync(
        Guid tenantId,
        Guid? campaignId,
        Guid? trackingAssetId,
        string? search,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool? attributedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>One attributed donation in full.</summary>
    Task<AttributedDonation?> GetAttributedDonationAsync(
        Guid tenantId, Guid donationId, CancellationToken cancellationToken);

    /// <summary>
    /// What each of a campaign's tracking assets has produced.
    ///
    /// THE FIGURE A TRACKING ASSET IS JUDGED ON. It is read from the donations rather than kept as
    /// a counter on the asset, because a counter and the donations it counts drift apart the first
    /// time a donation is refunded.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CampaignIncome>> GetTrackingAssetIncomeAsync(
        Guid tenantId, IReadOnlyCollection<Guid> trackingAssetIds, CancellationToken cancellationToken);
}
