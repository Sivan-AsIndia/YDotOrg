namespace YDot.PAY.Application.Common.Models;

/// <summary>
/// The answer from an action that changes state but has nothing richer to return.
///
/// <c>Version</c> is the record's new concurrency stamp, so a screen can issue a second action
/// without re-fetching. <c>PermittedActions</c> is what THIS caller may do next, decided by the
/// server from the record's state and the caller's permissions together.
/// </summary>
public sealed record OutcomeResponse(
    Guid Id,
    string Status,
    long Version,
    string Message,
    IReadOnlyList<string> PermittedActions);
