namespace YDot.PAY.Domain.Enums;

/// <summary>
/// Why money is going back.
///
/// A CONTROLLED LIST RATHER THAN FREE TEXT, because refund reasons are reported on: a finance
/// team needs to know how much went back as a duplicate charge versus how much was a donor
/// changing their mind, and free text cannot answer that.
/// </summary>
public enum RefundReason
{
    DonorRequested = 0,
    DuplicateCharge = 1,
    IncorrectAmount = 2,
    Fraudulent = 3,
    CampaignCancelled = 4,
    TestTransaction = 5,
    Other = 6
}
