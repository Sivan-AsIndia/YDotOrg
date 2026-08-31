namespace YDot.IAM.Application.DTOs;

/// <summary>Body for cancel, suspend, archive and revoke, where a reason is mandatory.</summary>
public sealed record ReasonRequest(string Reason, long ExpectedVersion);
