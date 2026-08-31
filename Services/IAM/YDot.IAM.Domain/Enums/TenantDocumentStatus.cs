namespace YDot.IAM.Domain.Enums;

/// <summary>Review state of one uploaded Organisation document.</summary>
public enum TenantDocumentStatus
{
    Uploaded = 0,
    UnderReview = 1,
    Accepted = 2,
    Rejected = 3,
    Superseded = 4
}
