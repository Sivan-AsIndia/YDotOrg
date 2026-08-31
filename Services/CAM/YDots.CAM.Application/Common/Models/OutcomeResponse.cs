namespace YDots.CAM.Application.Common.Models;

/// <summary>
/// The answer from an action that changes state but has nothing richer to return: submit,
/// approve, pause, resume, close.
///
/// <c>Version</c> is the one to pay attention to. It is the record new optimistic-concurrency
/// stamp, and the next call that changes the same record must send it back as
/// <c>ExpectedVersion</c> - so returning it here is what lets a screen issue a second action
/// without re-fetching, and what stops every second click answering 409.
///
/// <c>PermittedActions</c> is what THIS caller may do next, decided by the server from the
/// record state and the caller permissions together. Render buttons from it and they can never
/// disagree with what the API will allow.
/// </summary>
public sealed record OutcomeResponse(
    Guid Id,
    string Status,
    long Version,
    string Message,
    IReadOnlyList<string> PermittedActions);
