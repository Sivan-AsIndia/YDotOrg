namespace YDot.IAM.Application.DTOs;

/// <summary>
/// Body for a state transition endpoint. <c>ExpectedVersion</c> is what makes the transition
/// safe: the caller states the version they were looking at, and a mismatch is refused with
/// CONCURRENCY_CONFLICT rather than silently overwriting somebody else work.
/// </summary>
public sealed record TransitionRequest(long ExpectedVersion, string? Comment = null);
