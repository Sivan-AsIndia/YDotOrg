namespace YDot.IAM.Domain.Enums;

/// <summary>Whether a navigation node is offered. Hidden keeps the row and its mappings
/// intact while taking it off the screen, which is reversible; Retired is not.</summary>
public enum MenuStatus
{
    Draft = 0,
    Active = 1,
    Hidden = 2,
    Retired = 3
}
