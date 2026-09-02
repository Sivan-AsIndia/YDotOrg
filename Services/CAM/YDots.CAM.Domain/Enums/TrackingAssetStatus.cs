namespace YDots.CAM.Domain.Enums;

public enum TrackingAssetStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Active = 4,

    /// <summary>
    /// An Active asset somebody has ASKED to take down, waiting for an approver.
    ///
    /// THE MAKER DOES NOT DISABLE AN ASSET, and this state is what that rule needs. Taking an
    /// asset down stops a live QR code and a printed short link resolving, so it ends the
    /// campaign's ability to attribute anything that arrives through them - which is a decision,
    /// not an edit. The maker requests it here and an approver decides on it, exactly as a
    /// campaign closure goes Active -> Closing -> Closed rather than straight to Closed.
    ///
    /// THE ASSET IS STILL LIVE IN THIS STATE. `IsLiveAt` requires Active, so a requested disable
    /// does NOT stop redirects on its own - nothing about a pending request should change what a
    /// donor's scan does until the request is actually decided.
    /// </summary>
    DisableRequested = 6,

    Inactive = 5
}
