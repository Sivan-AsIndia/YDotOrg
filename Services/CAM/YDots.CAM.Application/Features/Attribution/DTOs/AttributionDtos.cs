using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Attribution.DTOs;

/// <summary>Filters the attribution explorer.</summary>
public sealed record AttributionSearchFilter
{
    /// <summary>Matches a donation reference, a donor name, or a tracking reference.</summary>
    public string? Search { get; init; }

    public Guid? CampaignId { get; init; }

    public Guid? TrackingAssetId { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>
    /// True for gifts traced to a tracking asset, false for those that were not, null for both.
    ///
    /// THE MOST USEFUL FILTER ON THE SCREEN. "Show me what my QR codes actually produced" and "show
    /// me what arrived without a link" are the two questions people come here with, and folding
    /// them together answers neither.
    /// </summary>
    public bool? AttributedOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>
/// A request to look at a donation's attribution again.
///
/// A REQUEST, NOT A CHANGE. Re-attributing a gift moves money between campaigns in every report
/// that follows it, so CAM records that somebody has asked and the correction itself is made where
/// the donation lives.
/// </summary>
public sealed record RequestAttributionCorrectionRequest
{
    public Guid DonationId { get; init; }

    /// <summary>What the requester believes it should be attributed to.</summary>
    public Guid? ProposedCampaignId { get; init; }

    public Guid? ProposedTrackingAssetId { get; init; }

    /// <summary>Why. Required - a correction request with no reasoning cannot be assessed.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>One attributed donation, as the explorer lists it.</summary>
public sealed record AttributionListItemResponse
{
    public Guid DonationId { get; init; }

    public string Reference { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; init; }

    public decimal Amount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public Guid? CampaignId { get; init; }

    public string CampaignCode { get; init; } = string.Empty;

    public string CampaignName { get; init; } = string.Empty;

    public Guid? TrackingAssetId { get; init; }

    public string TrackingReference { get; init; } = string.Empty;

    public TrackingAssetType? AssetType { get; init; }

    public string ChannelName { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string MediumName { get; init; } = string.Empty;

    public string DonorName { get; init; } = string.Empty;

    public Guid? DonorId { get; init; }

    /// <summary>
    /// Whether the gift was traced to a tracking asset.
    ///
    /// NOT THE SAME AS HAVING A CAMPAIGN. A donation recorded against a campaign by hand has a
    /// campaign and no attribution, and a report that treated the two alike would credit a QR code
    /// with money somebody gave over the telephone.
    /// </summary>
    public bool IsAttributed { get; init; }

    public string AttributionDescription { get; init; } = string.Empty;

    public bool HasOpenCorrectionRequest { get; init; }

    public IReadOnlyList<string> PermittedActions { get; init; } = [];
}

/// <summary>
/// One donation's full attribution trail.
///
/// THE POINT OF THE SCREEN. Somebody asking "why is this gift credited to that campaign?" needs
/// each hop - the link the donor followed, the asset it belonged to, the campaign that asset was
/// created for - laid out in order, because the answer is usually in one of the hops rather than at
/// the end.
/// </summary>
public sealed record AttributionDetailResponse
{
    public Guid DonationId { get; init; }

    public string Reference { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; init; }

    public decimal Amount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string DonorName { get; init; } = string.Empty;

    public Guid? DonorId { get; init; }

    public Guid? CampaignId { get; init; }

    public string CampaignCode { get; init; } = string.Empty;

    public string CampaignName { get; init; } = string.Empty;

    public string CampaignStatus { get; init; } = string.Empty;

    public Guid? TrackingAssetId { get; init; }

    public string TrackingReference { get; init; } = string.Empty;

    public TrackingAssetType? AssetType { get; init; }

    public string? AssetDestination { get; init; }

    public string ChannelName { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public string MediumName { get; init; } = string.Empty;

    public bool IsAttributed { get; init; }

    public string AttributionDescription { get; init; } = string.Empty;

    public bool HasOpenCorrectionRequest { get; init; }

    /// <summary>The hops, in the order they happened.</summary>
    public IReadOnlyList<AttributionTraceStepResponse> Trace { get; init; } = [];

    public IReadOnlyList<string> PermittedActions { get; init; } = [];
}

/// <summary>One step in an attribution trail.</summary>
public sealed record AttributionTraceStepResponse(
    string Key,
    string Title,
    string Caption,
    IReadOnlyList<AttributionTraceFieldResponse> Fields);

/// <summary>One labelled value inside a trace step.</summary>
public sealed record AttributionTraceFieldResponse(string Key, string Label, string Value, bool Copyable);

/// <summary>
/// How an organisation's income breaks down by source.
///
/// UNATTRIBUTED IS A ROW, not an omission. Most organisations find a large share of their income
/// arrives with no tracking behind it, and a breakdown that silently dropped it would make the
/// tracked channels look like the whole picture.
/// </summary>
public sealed record AttributionSummaryResponse
{
    public Guid? CampaignId { get; init; }

    public decimal TotalAmount { get; init; }

    public int TotalDonations { get; init; }

    public decimal AttributedAmount { get; init; }

    public int AttributedDonations { get; init; }

    public decimal UnattributedAmount { get; init; }

    public int UnattributedDonations { get; init; }

    /// <summary>The share of income that could be traced, as a percentage.</summary>
    public decimal AttributionRate { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public IReadOnlyList<AttributionBreakdownRow> ByChannel { get; init; } = [];

    public IReadOnlyList<AttributionBreakdownRow> BySource { get; init; } = [];

    public IReadOnlyList<AttributionBreakdownRow> ByMedium { get; init; } = [];

    public IReadOnlyList<AttributionBreakdownRow> ByAsset { get; init; } = [];
}

/// <summary>One line of an attribution breakdown.</summary>
public sealed record AttributionBreakdownRow(
    string Key,
    string Label,
    decimal Amount,
    int DonationCount,
    decimal SharePercentage);
