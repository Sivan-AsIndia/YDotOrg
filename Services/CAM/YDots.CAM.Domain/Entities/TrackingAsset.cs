using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A trackable destination for a campaign: a QR code, a short link, a UTM link or a landing
/// page.
///
/// THE TRACKING REFERENCE IS THE ATTRIBUTION KEY. It is what a donation intent carries back
/// from the public donation flow, and it is how a gift is credited to the campaign, channel,
/// source and medium that produced it. That makes it immutable once the asset is live -
/// changing it would orphan every donation already attributed through it.
/// </summary>
public sealed class TrackingAsset : TenantEntity, ICodedEntity
{
    /// <summary>Unique inside the Organisation. Appears in reporting beside the generated URL.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid CampaignId { get; set; }

    public TrackingAssetType AssetType { get; set; }

    public Guid ChannelId { get; set; }

    public string Destination { get; set; } = string.Empty;

    public Guid SourceId { get; set; }

    public Guid MediumId { get; set; }

    public string? ContentTag { get; set; }

    public TrackingAssetStatus Status { get; set; }

    public DateTimeOffset ActiveFrom { get; set; }

    public DateTimeOffset ActiveTo { get; set; }

    public string? GeneratedUrl { get; set; }

    /// <summary>
    /// The attribution key carried back by a donation intent. Server-generated and immutable
    /// once the asset leaves Draft.
    /// </summary>
    public string? TrackingReference { get; set; }

    public long UsageCount { get; set; }

    public decimal TotalReceived { get; set; }

    /// <summary>Who approved it, for the segregation-of-duties rule on tracking assets.</summary>
    public Guid? SubmittedByUserId { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public ICollection<TrackingAssetPlace> Places { get; set; } = [];

    /// <summary>
    /// Whether the asset is inside its own live window right now.
    ///
    /// SEPARATE FROM <see cref="Status"/>, and both matter. An Approved asset whose window has
    /// not opened yet should not resolve a scan, and neither should an Active one whose window
    /// has closed - the status says what an operator decided, the window says what the calendar
    /// says.
    /// </summary>
    public bool IsLiveAt(DateTimeOffset moment) =>
        Status == TrackingAssetStatus.Active && ActiveFrom <= moment && ActiveTo >= moment;

    /// <summary>The same independence rule campaigns use. See <see cref="Campaign.CanBeApprovedBy"/>.</summary>
    public bool CanBeApprovedBy(Guid userId) =>
        CreatedByUserId != userId && SubmittedByUserId != userId;
}
