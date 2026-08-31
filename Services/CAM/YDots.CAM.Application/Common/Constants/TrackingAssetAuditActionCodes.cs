namespace YDots.CAM.Application.Common.Constants;

public static class TrackingAssetAuditActionCodes
{
    public const string Created = "TRACKING_ASSET_CREATED";

    public const string Updated = "TRACKING_ASSET_UPDATED";

    public const string Submitted = "TRACKING_ASSET_SUBMITTED";

    public const string Approved = "TRACKING_ASSET_APPROVED";

    public const string Activated = "TRACKING_ASSET_ACTIVATED";

    public const string Deactivated = "TRACKING_ASSET_DEACTIVATED";

    /// <summary>
    /// A CSV of the tracking assets left the system.
    ///
    /// Audited more carefully than most exports: the file lists every live tracking reference,
    /// and a reference is the attribution key a donation carries. Anybody holding the list
    /// knows exactly which URL credits which campaign.
    /// </summary>
    public const string Exported = "TRACKING_ASSET_EXPORTED";
}
