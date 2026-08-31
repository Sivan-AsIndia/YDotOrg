using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Creating a country.
///
/// THE FIELD NAMES ARE <c>countryCode</c> AND <c>countryName</c>, NOT <c>code</c> AND
/// <c>name</c>. The entity calls them Code and Name, because every master in the catalogue
/// does and the shared base is what makes the generic repository possible. The wire contract
/// keeps the domain wording the Angular Masters screens were built against, so migrating the
/// service did not force a rewrite of a form that was already correct.
///
/// No TenantId field. A country created here is stamped with the caller's Organisation by the
/// DbContext and is visible only to them; the shared ISO catalogue is seeded, not posted.
/// </summary>
public sealed record CreateCountryRequest(
    string CountryCode,
    string CountryName,
    string Iso2,
    string? OfficialName = null,
    GeographicRegion? Region = null,
    string? Iso3 = null,
    string? NumericCode = null,
    string? DefaultCurrencyCode = null,
    bool HasStates = true,
    string? PostalCodePattern = null,
    string? PhoneCountryCode = null,
    MasterDataStatus Status = MasterDataStatus.Active,
    int SortOrder = 0,
    string? Notes = null);

/// <summary>
/// Editing a country.
///
/// Every field is nullable and means "leave it alone" when omitted, EXCEPT
/// <c>ExpectedVersion</c>. That is the pattern the IAM update requests already use, and it is
/// what lets the detail screen send only what the operator actually touched.
/// </summary>
public sealed record UpdateCountryRequest(
    long ExpectedVersion,
    string? CountryName = null,
    string? OfficialName = null,
    GeographicRegion? Region = null,
    string? Iso2 = null,
    string? Iso3 = null,
    string? NumericCode = null,
    string? DefaultCurrencyCode = null,
    bool? HasStates = null,
    string? PostalCodePattern = null,
    string? PhoneCountryCode = null,
    int? SortOrder = null,
    string? Notes = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the country grid. Deliberately narrower than the detail response.</summary>
public sealed record CountryListItemResponse(
    Guid Id,
    Guid? TenantId,
    string CountryCode,
    string CountryName,
    string? OfficialName,
    GeographicRegion? Region,
    string Iso2,
    string? Iso3,
    string FlagEmoji,
    string? DefaultCurrencyCode,
    string? PhoneCountryCode,
    bool HasStates,
    MasterDataStatus Status,
    string StatusDescription,

    /// <summary>Kept for the existing grid, which binds a boolean toggle to it.</summary>
    bool IsActive,

    bool IsPlatformRow,
    int SortOrder,
    int StateProvinceCount,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full country record behind the detail panel.</summary>
public sealed record CountryDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string CountryCode,
    string CountryName,
    string? OfficialName,
    GeographicRegion? Region,
    string Iso2,
    string? Iso3,
    string? NumericCode,
    string FlagEmoji,
    string? DefaultCurrencyCode,
    bool HasStates,
    string? PostalCodePattern,
    string? PhoneCountryCode,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    string? Notes,
    int StateProvinceCount,
    int CityCount,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,

    /// <summary>
    /// What the record's STATE allows, before permission is considered. A platform row offers
    /// no Edit and no Delete to a Tenant caller, so the screen does not draw a button that
    /// would answer 403.
    /// </summary>
    IReadOnlyList<string> PermittedActions);
