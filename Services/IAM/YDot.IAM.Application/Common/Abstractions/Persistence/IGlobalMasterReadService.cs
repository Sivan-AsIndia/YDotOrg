using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read-side projections for the five Masters grids and their pickers.
///
/// SEPARATE FROM <see cref="IGlobalMasterRepository"/> FOR THE USUAL REASON: a repository
/// loads a tracked aggregate so it can be changed, while a grid wants a dozen columns from
/// three tables for twenty rows. Loading twenty tracked Country aggregates with their states
/// and cities to draw a list is how a screen ends up issuing forty queries.
///
/// The counts these projections carry — how many states a country has, how many countries use
/// a currency — are computed IN the projection rather than fetched per row afterwards. That
/// is the difference between one query and twenty-one.
/// </summary>
public interface IGlobalMasterReadService
{
    // ---- Countries -------------------------------------------------------------------

    Task<PagedResponse<CountryListItemResponse>> SearchCountriesAsync(
        CountrySearchFilter filter, CancellationToken cancellationToken);

    Task<CountryDetailResponse?> GetCountryDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CountryExportRow>> GetCountryExportRowsAsync(
        CountrySearchFilter filter, CancellationToken cancellationToken);

    // ---- States and provinces --------------------------------------------------------------

    Task<PagedResponse<StateProvinceListItemResponse>> SearchStateProvincesAsync(
        StateProvinceSearchFilter filter, CancellationToken cancellationToken);

    Task<StateProvinceDetailResponse?> GetStateProvinceDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StateProvinceExportRow>> GetStateProvinceExportRowsAsync(
        StateProvinceSearchFilter filter, CancellationToken cancellationToken);

    // ---- Cities ----------------------------------------------------------------------------------

    Task<PagedResponse<CityListItemResponse>> SearchCitiesAsync(
        CitySearchFilter filter, CancellationToken cancellationToken);

    Task<CityDetailResponse?> GetCityDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CityExportRow>> GetCityExportRowsAsync(
        CitySearchFilter filter, CancellationToken cancellationToken);

    // ---- Currencies ------------------------------------------------------------------------------------

    Task<PagedResponse<CurrencyListItemResponse>> SearchCurrenciesAsync(
        CurrencySearchFilter filter, CancellationToken cancellationToken);

    Task<CurrencyDetailResponse?> GetCurrencyDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrencyExportRow>> GetCurrencyExportRowsAsync(
        CurrencySearchFilter filter, CancellationToken cancellationToken);

    // ---- Time zones ------------------------------------------------------------------------------------

    Task<PagedResponse<TimeZoneListItemResponse>> SearchTimeZonesAsync(
        TimeZoneSearchFilter filter, CancellationToken cancellationToken);

    Task<TimeZoneDetailResponse?> GetTimeZoneDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TimeZoneExportRow>> GetTimeZoneExportRowsAsync(
        TimeZoneSearchFilter filter, CancellationToken cancellationToken);

    // ---- Pickers -----------------------------------------------------------------------------------------

    /// <summary>
    /// Every dropdown the Masters screens need, in one payload.
    ///
    /// <paramref name="countryId"/> narrows the state list to one country when the caller
    /// already knows it, which is the case on the City form. Null returns every state, which
    /// is what the State grid's own filter needs.
    /// </summary>
    Task<GlobalMasterReferenceDataResponse> GetReferenceDataAsync(
        Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Active states beneath one country, for the cascading City form.</summary>
    Task<IReadOnlyList<MasterLookupResponse>> LookupStateProvincesAsync(
        Guid countryId, CancellationToken cancellationToken);

    /// <summary>Active cities beneath one state.</summary>
    Task<IReadOnlyList<MasterLookupResponse>> LookupCitiesAsync(
        Guid stateProvinceId, CancellationToken cancellationToken);

    // ---- The address-form pickers, usable from any page ------------------------------------

    /// <summary>
    /// Active countries, each carrying its default currency and primary time zone.
    ///
    /// Richer than <see cref="LookupStateProvincesAsync"/>'s rows because a country selection
    /// has consequences elsewhere on the form, and a second round trip to discover them is a
    /// flicker the person can see.
    /// </summary>
    Task<IReadOnlyList<CountryLookupResponse>> LookupCountriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Active cities, narrowed by state when one is given and by country when only that is.
    ///
    /// BOTH ARGUMENTS OPTIONAL. A form that collects a city without collecting a state — and
    /// several do — still gets a usable list rather than an exception.
    /// </summary>
    Task<IReadOnlyList<MasterLookupResponse>> LookupCitiesAsync(
        Guid? countryId, Guid? stateProvinceId, CancellationToken cancellationToken);

    /// <summary>
    /// Active currencies. When <paramref name="countryId"/> is given, that country's default is
    /// flagged and sorted first; the list itself is never narrowed, because a donation may
    /// legitimately be taken in a currency other than the country's own.
    /// </summary>
    Task<IReadOnlyList<CurrencyLookupResponse>> LookupCurrenciesAsync(
        Guid? countryId, CancellationToken cancellationToken);

    /// <summary>
    /// Active time zones, narrowed to one country's when it has any mapped.
    ///
    /// NEVER THROWS AND NEVER RETURNS EMPTY, which is the entire contract. A null
    /// <paramref name="countryId"/> — the page that needs a zone but never asks for a country —
    /// returns the full catalogue. An unknown id, or a country with no zones mapped yet, also
    /// returns the full catalogue rather than a 404 or an empty dropdown. The returned flag says
    /// which of the two happened so the caller can label the list truthfully.
    /// </summary>
    Task<(IReadOnlyList<TimeZoneLookupResponse> Zones, bool IsCountryFiltered)> LookupTimeZonesAsync(
        Guid? countryId, CancellationToken cancellationToken);

    /// <summary>
    /// Active languages, narrowed to one country's when it has any mapped.
    ///
    /// THE SAME CONTRACT AS <see cref="LookupTimeZonesAsync"/>, deliberately: never throws and
    /// never returns empty. A null <paramref name="countryId"/> — user creation and the setup
    /// wizard, neither of which collects a country — returns the full catalogue, and that is a
    /// supported case rather than a degraded one. An unknown id, or a country with no languages
    /// mapped, also returns the full catalogue rather than a 404 or an empty dropdown. The flag
    /// says which of the two happened so the caller can label the list truthfully.
    /// </summary>
    Task<(IReadOnlyList<LanguageLookupResponse> Languages, bool IsCountryFiltered)> LookupLanguagesAsync(
        Guid? countryId, CancellationToken cancellationToken);

    /// <summary>Every address-form picker in one payload. See <see cref="GeoLookupResponse"/>.</summary>
    Task<GeoLookupResponse> GetGeoLookupAsync(
        Guid? countryId, Guid? stateProvinceId, CancellationToken cancellationToken);
}
