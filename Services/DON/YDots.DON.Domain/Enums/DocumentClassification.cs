namespace YDots.DON.Domain.Enums;

/// <summary>
/// How sensitive a linked document is. The Documents panel on Donor 360 is confidential, so
/// the classification decides whether a row is even listed to a caller.
/// </summary>
public enum DocumentClassification
{
    Internal = 1,
    Restricted = 2,
    Confidential = 3
}
