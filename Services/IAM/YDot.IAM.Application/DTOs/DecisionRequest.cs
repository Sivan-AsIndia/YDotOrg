namespace YDot.IAM.Application.DTOs;

/// <summary>
/// Body for an approve/reject endpoint. <c>Reason</c> is required when
/// <c>Approved</c> is false — a refusal the person cannot act on is not a decision, it is a
/// dead end.
/// </summary>
public sealed record DecisionRequest(bool Approved, long ExpectedVersion, string? Reason = null);
