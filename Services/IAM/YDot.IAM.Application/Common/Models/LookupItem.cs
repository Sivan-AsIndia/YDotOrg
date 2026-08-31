namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// One option in a dropdown. <c>Code</c> is present because every master now carries one and
/// the screens show it beside the name, which is how an operator tells two similarly named
/// records apart.
/// </summary>
public sealed record LookupItem(Guid Id, string Code, string Name, bool IsActive = true, string? Description = null);
