namespace YDot.IAM.Domain.Enums;

/// <summary>Lifecycle of a batch of access reviews issued together.</summary>
public enum AccessReviewCampaignStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2,
    Cancelled = 3
}
