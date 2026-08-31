namespace YDots.DON.Application.DTOs;

/// <summary>Shared request body for every action that only needs a named reason (section 8).</summary>
public sealed class ReasonRequest
{
    /// <summary>Why the action is being taken. 10 to 2000 characters.</summary>
    public string Reason { get; set; } = string.Empty;

    public long? ExpectedVersion { get; set; }
}
