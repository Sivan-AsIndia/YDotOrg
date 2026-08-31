namespace YDots.DON.Application.Common.Models;

/// <summary>Generic value and label pair used to fill every dropdown and authorised lookup.</summary>
public sealed record LookupItem(string Value, string Label, string? Description = null);
