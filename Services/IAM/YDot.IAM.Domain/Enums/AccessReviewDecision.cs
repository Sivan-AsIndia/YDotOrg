namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.5: Retain|Modify|Revoke. Modify and Revoke both require a reason.</summary>
public enum AccessReviewDecision
{
    Retain = 0,
    Modify = 1,
    Revoke = 2
}
