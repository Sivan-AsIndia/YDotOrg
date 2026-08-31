using System.Globalization;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>Manual mapping for the Cities slice.</summary>
public static class CityMappingConfig
{
    /// <summary>
    /// Builds a new City.
    ///
    /// THE COUNTRY COMES FROM THE STATE, never from the request. That is the whole reason the
    /// denormalised <c>City.CountryId</c> is safe: there is no path by which a caller can pair
    /// a Maharashtra state with a Canadian country, because the caller never gets to name the
    /// country at all.
    /// </summary>
    public static City ToEntity(
        this CreateCityRequest request,
        StateProvince state,
        GeoCoordinateValue? coordinates,
        Guid? tenantId,
        Guid businessUnitId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        return new City
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            Code = CodeValue.Parse(request.CityCode).Value,
            Name = request.CityName.Trim(),
            DisplayName = Clean(request.DisplayName),
            StateProvinceId = state.Id,
            CountryId = state.CountryId,
            DefaultPostalCodePattern = Clean(request.DefaultPostalCodePattern),
            IsMetro = request.IsMetro,
            Latitude = coordinates?.Latitude,
            Longitude = coordinates?.Longitude,
            Status = request.Status,
            SortOrder = request.SortOrder,
            Notes = Clean(request.Notes)
        };
    }

    /// <summary>
    /// Applies an update in place.
    ///
    /// <paramref name="coordinates"/> is the already-parsed pair, or null when the request
    /// left both fields alone. <c>ClearCoordinates</c> is checked FIRST and wins, because it
    /// is the only way to un-geocode a city - a null latitude on its own has to keep meaning
    /// "unchanged" or a partial update would silently wipe the pair.
    /// </summary>
    public static void ApplyTo(this UpdateCityRequest request, City city, GeoCoordinateValue? coordinates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(city);

        if (!string.IsNullOrWhiteSpace(request.CityName))
        {
            city.Name = request.CityName.Trim();
        }

        if (request.DisplayName is not null)
        {
            city.DisplayName = Clean(request.DisplayName);
        }

        if (request.DefaultPostalCodePattern is not null)
        {
            city.DefaultPostalCodePattern = Clean(request.DefaultPostalCodePattern);
        }

        if (request.IsMetro.HasValue)
        {
            city.IsMetro = request.IsMetro.Value;
        }

        if (request.ClearCoordinates)
        {
            city.Latitude = null;
            city.Longitude = null;
        }
        else if (coordinates is not null)
        {
            city.Latitude = coordinates.Latitude;
            city.Longitude = coordinates.Longitude;
        }

        if (request.SortOrder.HasValue)
        {
            city.SortOrder = request.SortOrder.Value;
        }

        if (request.Notes is not null)
        {
            city.Notes = Clean(request.Notes);
        }
    }

    /// <summary>One row of the grid.</summary>
    public static CityListItemResponse ToListItemResponse(
        this City city,
        string stateProvinceCode,
        string stateProvinceName,
        string countryCode,
        string countryName)
    {
        ArgumentNullException.ThrowIfNull(city);

        return new CityListItemResponse(
            city.Id,
            city.TenantId,
            city.Code,
            city.Name,
            city.DisplayName,
            city.StateProvinceId,
            stateProvinceCode,
            stateProvinceName,
            city.CountryId,
            countryCode,
            countryName,
            city.IsMetro,
            city.Latitude,
            city.Longitude,
            city.Status,
            GlobalMasterMappingConfig.DescribeStatus(city.Status),
            GlobalMasterMappingConfig.IsActiveFlag(city.Status),
            city.IsPlatformRow,
            city.SortOrder,
            city.UpdatedAtUtc,
            city.Version);
    }

    /// <summary>
    /// The detail panel.
    ///
    /// A city is the LEAF of the geography, so nothing hangs beneath it and the dependent
    /// count passed to the permitted-actions rule is always zero. Delete is therefore always
    /// offered on a city the caller owns - which is correct, and worth stating because the
    /// other four masters all have a dependency that can block it.
    /// </summary>
    public static CityDetailResponse ToDetailResponse(
        this City city,
        string stateProvinceCode,
        string stateProvinceName,
        string countryCode,
        string countryName,
        bool isSuperAdmin)
    {
        ArgumentNullException.ThrowIfNull(city);

        return new CityDetailResponse(
            city.Id,
            city.TenantId,
            city.BusinessUnitId,
            city.Code,
            city.Name,
            city.DisplayName,
            city.StateProvinceId,
            stateProvinceCode,
            stateProvinceName,
            city.CountryId,
            countryCode,
            countryName,
            city.DefaultPostalCodePattern,
            city.IsMetro,
            city.Latitude,
            city.Longitude,
            city.HasCoordinates,
            city.Status,
            GlobalMasterMappingConfig.DescribeStatus(city.Status),
            GlobalMasterMappingConfig.IsActiveFlag(city.Status),
            city.IsPlatformRow,
            city.SortOrder,
            city.Notes,
            city.CreatedAtUtc,
            city.CreatedByUserId,
            city.UpdatedAtUtc,
            city.UpdatedByUserId,
            city.Version,
            GlobalMasterMappingConfig.PermittedActionsFor(city, isSuperAdmin, dependentCount: 0));
    }

    /// <summary>One option in a city picker.</summary>
    public static MasterLookupResponse ToLookupResponse(this City city)
    {
        ArgumentNullException.ThrowIfNull(city);

        return new MasterLookupResponse(
            city.Id,
            city.Code,
            city.DisplayName ?? city.Name,
            city.Status,
            city.IsPlatformRow,
            city.SortOrder);
    }

    /// <summary>One line of the CSV export.</summary>
    public static CityExportRow ToExportRow(
        this City city,
        string stateProvinceCode,
        string stateProvinceName,
        string countryCode,
        string countryName)
    {
        ArgumentNullException.ThrowIfNull(city);

        return new CityExportRow(
            city.Code,
            city.Name,
            stateProvinceCode,
            stateProvinceName,
            countryCode,
            countryName,
            city.IsMetro ? "Yes" : "No",
            city.Latitude?.ToString(CultureInfo.InvariantCulture),
            city.Longitude?.ToString(CultureInfo.InvariantCulture),
            city.Status.ToString(),
            GlobalMasterMappingConfig.DescribeScope(city.IsPlatformRow),
            city.SortOrder.ToString(CultureInfo.InvariantCulture));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
