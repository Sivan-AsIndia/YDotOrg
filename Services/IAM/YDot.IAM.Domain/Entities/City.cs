using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A city, town or village inside a <see cref="StateProvince"/>.
///
/// IT CARRIES <see cref="CountryId"/> AS WELL AS <see cref="StateProvinceId"/>, which is
/// denormalised and deliberate: every country-level roll-up would otherwise join through
/// StateProvince to reach the country, and the city table is the largest of the five. The
/// handler keeps the two in step by taking the country FROM the chosen state rather than
/// from the request, so the denormalisation cannot drift.
/// </summary>
public class City : GlobalMasterEntity
{
    public string? DisplayName { get; set; }

    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public Guid StateProvinceId { get; set; }

    public StateProvince StateProvince { get; set; } = default!;

    /// <summary>Narrower than the state pattern where a city has its own postal convention.</summary>
    public string? DefaultPostalCodePattern { get; set; }

    /// <summary>Marks a tier-1 city. Used for territory planning and campaign targeting.</summary>
    public bool IsMetro { get; set; }

    /// <summary>Decimal degrees, positive north. Null where the city has not been geocoded.</summary>
    public decimal? Latitude { get; set; }

    /// <summary>Decimal degrees, positive east.</summary>
    public decimal? Longitude { get; set; }

    /// <summary>True once both coordinates are present, so a map can decide whether to plot it.</summary>
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;
}
