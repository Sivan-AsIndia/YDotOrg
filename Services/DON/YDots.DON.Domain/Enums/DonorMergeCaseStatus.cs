namespace YDots.DON.Domain.Enums;

/// <summary>Module-defined lifecycle enum for DonorMergeCase (section 3.5).</summary>
public enum DonorMergeCaseStatus
{
    Active = 1,
    UnderReview = 2,
    Merged = 3,
    Linked = 4,
    KeptSeparate = 5,
    Rejected = 6
}
