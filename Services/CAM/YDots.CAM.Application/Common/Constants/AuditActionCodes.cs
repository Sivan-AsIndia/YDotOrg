namespace YDots.CAM.Application.Common.Constants;

public static class AuditActionCodes
{
    public const string CampaignCreated = "CAMPAIGN_CREATED";
    public const string CampaignUpdated = "CAMPAIGN_UPDATED";
    public const string CampaignSubmitted = "CAMPAIGN_SUBMITTED";
    public const string CampaignApproved = "CAMPAIGN_APPROVED";
    public const string CampaignActivated = "CAMPAIGN_ACTIVATED";
    public const string CampaignPaused = "CAMPAIGN_PAUSED";
    public const string CampaignResumed = "CAMPAIGN_RESUMED";
    public const string CampaignCloseRequested = "CAMPAIGN_CLOSE_REQUESTED";
    public const string CampaignCloseApproved = "CAMPAIGN_CLOSE_APPROVED";
    public const string CampaignDraftDeleted = "CAMPAIGN_DRAFT_DELETED";

    /// <summary>
    /// A CSV of the register left the system.
    ///
    /// Worth its own code because a campaign export carries targets, budgets and donor-facing
    /// wording - and unlike a grid load, it produces a file that outlives the session.
    /// </summary>
    public const string CampaignExported = "CAMPAIGN_EXPORTED";
}
