using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A name/value pair carried by one placement, for whatever a particular campaign needs to
/// record that the fixed columns do not cover.
///
/// A pure child row, so it stays on <see cref="BaseEntity"/>: it lives and dies with its
/// placement and has no independent history worth five audit columns.
/// </summary>
public sealed class TrackingAssetCustomField : BaseEntity
{
    public Guid TrackingAssetPlaceId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public TrackingAssetPlace Place { get; set; } = default!;
}
