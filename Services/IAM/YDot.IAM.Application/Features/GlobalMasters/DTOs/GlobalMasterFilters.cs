using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

/// <summary>
/// Filter for the country grid.
///
/// <c>Search</c> and <c>Status</c> come from <c>GlobalMasterSearchFilter</c>; what is added
/// here is the country-specific narrowing the screen offers.
/// </summary>
public sealed class CountrySearchFilter : GlobalMasterSearchFilter
{
    public GeographicRegion? Region { get; set; }

    /// <summary>Countries whose addresses carry a subdivision. Drives the State form's picker.</summary>
    public bool? HasStates { get; set; }

    /// <summary>Countries defaulting to one currency. Answers "what breaks if I retire INR?".</summary>
    public string? DefaultCurrencyCode { get; set; }
}

/// <summary>Filter for the state grid.</summary>
public sealed class StateProvinceSearchFilter : GlobalMasterSearchFilter
{
    public Guid? CountryId { get; set; }

    public JurisdictionType? JurisdictionType { get; set; }

    public bool? IsFederalJurisdiction { get; set; }
}

/// <summary>Filter for the city grid.</summary>
public sealed class CitySearchFilter : GlobalMasterSearchFilter
{
    public Guid? CountryId { get; set; }

    public Guid? StateProvinceId { get; set; }

    public bool? IsMetro { get; set; }

    /// <summary>Cities with no coordinates, so a gap in the geocoding can be worked through.</summary>
    public bool? HasCoordinates { get; set; }
}

/// <summary>Filter for the currency grid.</summary>
public sealed class CurrencySearchFilter : GlobalMasterSearchFilter
{
    public CurrencyType? CurrencyType { get; set; }
}

/// <summary>Filter for the time-zone grid.</summary>
public sealed class TimeZoneSearchFilter : GlobalMasterSearchFilter
{
    public bool? SupportsDaylightSaving { get; set; }

    public bool? IsDefaultRecommended { get; set; }
}

// =====================================================================================
// Export rows
// =====================================================================================
//
// Flat, all-string records rather than the list DTOs. A CSV column has no notion of an enum
// or a nullable, so the shaping happens once here rather than being re-decided by whatever
// writes the file.

/// <summary>One line of the country export.</summary>
public sealed record CountryExportRow(
    string CountryCode,
    string CountryName,
    string? OfficialName,
    string? Region,
    string Iso2,
    string? Iso3,
    string? NumericCode,
    string? DefaultCurrencyCode,
    string HasStates,
    string? PhoneCountryCode,
    string Status,
    string Scope,
    string SortOrder);

/// <summary>One line of the state export.</summary>
public sealed record StateProvinceExportRow(
    string StateProvinceCode,
    string StateProvinceName,
    string CountryCode,
    string CountryName,
    string JurisdictionType,
    string IsFederalJurisdiction,
    string? GstStateCode,
    string? DefaultTimeZone,
    string Status,
    string Scope,
    string SortOrder);

/// <summary>One line of the city export.</summary>
public sealed record CityExportRow(
    string CityCode,
    string CityName,
    string StateProvinceCode,
    string StateProvinceName,
    string CountryCode,
    string CountryName,
    string IsMetro,
    string? Latitude,
    string? Longitude,
    string Status,
    string Scope,
    string SortOrder);

/// <summary>One line of the currency export.</summary>
public sealed record CurrencyExportRow(
    string CurrencyCode,
    string CurrencyName,
    string? NumericCode,
    string CurrencyType,
    string? Symbol,
    string SymbolPosition,
    string DecimalPlaces,
    string? MinorUnitName,
    string RoundingMode,
    string? RoundingStep,
    string Status,
    string Scope,
    string SortOrder);

/// <summary>One line of the time-zone export.</summary>
public sealed record TimeZoneExportRow(
    string TimeZoneKey,
    string DisplayName,
    string? ShortName,
    string OffsetDisplay,
    string StandardUtcOffsetMinutes,
    string SupportsDaylightSaving,
    string IsDefaultRecommended,
    string Status,
    string Scope,
    string SortOrder);
