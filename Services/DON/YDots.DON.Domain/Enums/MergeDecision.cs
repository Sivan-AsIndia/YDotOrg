namespace YDots.DON.Domain.Enums;

/// <summary>SCR-DON-004 decision: compare candidates and decide link, merge or keep separate.</summary>
public enum MergeDecision
{
    Merge = 1,
    Link = 2,
    KeepSeparate = 3,
    Reject = 4
}
