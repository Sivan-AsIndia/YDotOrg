namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// The place a campaign runs in, resolved to names.
///
/// EVERY FIELD IS NULLABLE because every id behind it is optional or may not resolve. A campaign
/// created before state and city became mandatory has neither, and a master row that has since
/// been removed leaves a name behind that nothing can supply. A detail screen showing a blank
/// city is correct in both cases; one that refuses to load is not.
/// </summary>
public sealed record PlaceNames(string? CountryName, string? StateName, string? CityName);

/// <summary>
/// Country, state and city names, read from the IAM master catalogue over the shared database.
///
/// WHY CAM NEEDS THIS AT ALL. A campaign stores <c>CountryId</c>, <c>StateId</c> and
/// <c>CityId</c> as loose ids into <c>gm_countries</c>, <c>gm_state_provinces</c> and
/// <c>gm_cities</c> - deliberately not foreign keys, because CAM and IAM are separately
/// deployable. The campaign detail returned those three Guids and nothing else, so the Location
/// row on the screen read "-" for every campaign ever created. The client could have fetched the
/// three master lists itself, but that is three more round trips before one panel can be drawn,
/// and it puts the burden on every consumer of the API rather than on the API.
///
/// READ-ONLY, WITHOUT EXCEPTION, and it does not throw: a name is decoration, and a campaign
/// that cannot reach the geography tables should show its dates and its owners rather than fail
/// to open. That is the same choice <see cref="IFinancialDirectory"/> makes, and the opposite of
/// <see cref="IPeopleDirectory.GetExistingUserIdsAsync"/>, whose job is to refuse.
/// </summary>
public interface IGeographyDirectory
{
    /// <summary>The names behind one campaign's geography ids. Any of them may come back null.</summary>
    Task<PlaceNames> GetPlaceNamesAsync(
        Guid countryId, Guid? stateId, Guid? cityId, CancellationToken cancellationToken);

    /// <summary>
    /// State names by id, for a page that shows several.
    ///
    /// A set rather than an id, so a tracking asset with six placements in four states is one
    /// query rather than four.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetStateNamesAsync(
        IReadOnlyCollection<Guid> stateIds, CancellationToken cancellationToken);

    /// <summary>City names by id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCityNamesAsync(
        IReadOnlyCollection<Guid> cityIds, CancellationToken cancellationToken);
}
