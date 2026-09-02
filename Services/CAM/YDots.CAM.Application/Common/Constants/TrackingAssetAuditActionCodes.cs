namespace YDots.CAM.Application.Common.Constants;

public static class TrackingAssetAuditActionCodes
{
    public const string Created = "TRACKING_ASSET_CREATED";

    public const string Updated = "TRACKING_ASSET_UPDATED";

    public const string Submitted = "TRACKING_ASSET_SUBMITTED";

    public const string Approved = "TRACKING_ASSET_APPROVED";

    public const string Activated = "TRACKING_ASSET_ACTIVATED";

    public const string DisableRequested = "TRACKING_ASSET_DISABLE_REQUESTED";

    public const string Deactivated = "TRACKING_ASSET_DEACTIVATED";

    /// <summary>
    /// An unused DRAFT asset was destroyed.
    ///
    /// The audit row outlives the asset, deliberately: this is the only delete in the module, and
    /// a record that something was removed - by whom, and for what stated reason - is what makes
    /// it safe to offer at all.
    /// </summary>
    public const string DraftDeleted = "TRACKING_ASSET_DRAFT_DELETED";

    /// <summary>
    /// A CSV of the tracking assets left the system.
    ///
    /// Audited more carefully than most exports: the file lists every live tracking reference,
    /// and a reference is the attribution key a donation carries. Anybody holding the list
    /// knows exactly which URL credits which campaign.
    /// </summary>
    public const string Exported = "TRACKING_ASSET_EXPORTED";
}
