namespace YDot.IAM.Application.DTOs;

/// <summary>
/// The answer from an action that changes state but has nothing meaningful to return.
/// Carries the new status and version so the screen can refresh its buttons without a second
/// round trip.
/// </summary>
public sealed record OutcomeResponse(
    Guid Id,
    string Status,
    long Version,
    string Message,
    IReadOnlyList<string> PermittedActions);
