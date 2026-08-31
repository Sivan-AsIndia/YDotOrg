namespace YDots.DON.Application.DTOs;

/// <summary>Shared request body for a lifecycle transition such as submit (section 8).</summary>
public sealed class TransitionRequest
{
    public string? Comment { get; set; }

    /// <summary>Version the caller had on screen. Used for the optimistic concurrency check.</summary>
    public long? ExpectedVersion { get; set; }
}
