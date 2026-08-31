namespace YDots.DON.Application.DTOs;

/// <summary>
/// The persistent success outcome every screen must show (UI section 4.x.4): stable reference,
/// resulting state, effective time, remaining dependency and the next permitted action.
/// A toast may support this but can never replace it, which is why it is a real payload.
/// </summary>
public sealed record OutcomeResponse(
    string Reference,
    string ResultingState,
    DateTimeOffset EffectiveAtUtc,
    string Message,
    string? NextAction = null,
    string? PendingDependency = null,
    string? CorrelationId = null);
