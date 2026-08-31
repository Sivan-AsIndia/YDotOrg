namespace YDots.DON.Domain.Enums;

/// <summary>
/// The stages the "Donation totals by stage" panel groups by. The amounts themselves are owned
/// by the donations section; DON only keeps a projection of them so the 360 view can be drawn
/// without a synchronous call to another service.
/// </summary>
public enum DonationStage
{
    Pledged = 1,
    Committed = 2,
    Received = 3,
    Refunded = 4,
    Outstanding = 5
}
