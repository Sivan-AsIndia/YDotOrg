namespace YDot.PAY.Application.Common.Results;

/// <summary>One field level validation message produced by FluentValidation.</summary>
public sealed record ValidationError(string Field, string Message);
