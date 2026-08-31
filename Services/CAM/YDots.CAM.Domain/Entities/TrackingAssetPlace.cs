using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// One physical or logical placement of a tracking asset: the poster in a particular hall, the
/// link in a particular newsletter.
///
/// Placements are what let one QR code be printed in six locations and still report which
/// location produced a gift.
/// </summary>
public sealed class TrackingAssetPlace : AuditEntity
{
    public Guid TrackingAssetId { get; set; }

    public string PlaceName { get; set; } = string.Empty;

    /// <summary>Rows in the IAM geography master. Not FKs - CAM and IAM deploy separately.</summary>
    public Guid? CityId { get; set; }

    public Guid? StateId { get; set; }

    public string Destination { get; set; } = string.Empty;

    public TrackingAsset TrackingAsset { get; set; } = default!;

    public ICollection<TrackingAssetCustomField> CustomFields { get; set; } = [];
}
