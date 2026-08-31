namespace YDot.IAM.Domain.Enums;

/// <summary>
/// What a first-level country subdivision actually is. India has States and Union
/// Territories, Canada has Provinces and Territories, and the tax treatment differs between
/// them — so the distinction is data rather than a naming convention.
///
/// <see cref="Other"/> carries a free-text description on the entity, so an unusual
/// jurisdiction does not block a country from being set up.
/// </summary>
public enum JurisdictionType
{
    State = 0,
    UnionTerritory = 1,
    Province = 2,
    Territory = 3,
    Region = 4,
    District = 5,
    Prefecture = 6,
    Other = 7
}
