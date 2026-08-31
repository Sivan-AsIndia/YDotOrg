namespace YDots.DON.Application.DTOs;

/// <summary>Shared request body for an approve or reject decision (section 8).</summary>
public sealed class DecisionRequest
{
    /// <summary>True approves, false rejects.</summary>
    public bool Approved { get; set; }

    /// <summary>Required when the decision is a rejection. 10 to 2000 characters.</summary>
    public string? Reason { get; set; }

    public long? ExpectedVersion { get; set; }
}
