using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Creating a city.
///
/// THERE IS NO <c>CountryId</c>. The city's country is taken from the chosen state, which is
/// the only way the denormalised column on the entity can be guaranteed to agree with it. A
/// caller who could send both could send a Maharashtra city in Canada, and nothing downstream
/// would ever notice.
/// </summary>
public sealed record CreateCityRequest(
    string CityCode,
    string CityName,
    Guid StateProvinceId,
    string? DisplayName = null,
    string? DefaultPostalCodePattern = null,
    bool IsMetro = false,
    decimal? Latitude = null,
    decimal? Longitude = null,
    MasterDataStatus Status = MasterDataStatus.Active,
    int SortOrder = 0,
    string? Notes = null);

/// <summary>
/// Editing a city.
///
/// <c>StateProvinceId</c> is absent for the same reason <c>CountryId</c> is absent from the
/// state update: re-parenting silently rewrites the geography of every address already
/// pointing at the row.
/// </summary>
public sealed record UpdateCityRequest(
    long ExpectedVersion,
    string? CityName = null,
    string? DisplayName = null,
    string? DefaultPostalCodePattern = null,
    bool? IsMetro = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    int? SortOrder = null,
    string? Notes = null,

    /// <summary>
    /// Clears both coordinates. Needed because a null Latitude already means "unchanged", so
    /// there would otherwise be no way to un-geocode a city that was geocoded wrongly.
    /// </summary>
    bool ClearCoordinates = false);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the city grid.</summary>
public sealed record CityListItemResponse(
    Guid Id,
    Guid? TenantId,
    string CityCode,
    string CityName,
    string? DisplayName,
    Guid StateProvinceId,
    string StateProvinceCode,
    string StateProvinceName,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    bool IsMetro,
    decimal? Latitude,
    decimal? Longitude,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full city record.</summary>
public sealed record CityDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string CityCode,
    string CityName,
    string? DisplayName,
    Guid StateProvinceId,
    string StateProvinceCode,
    string StateProvinceName,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    string? DefaultPostalCodePattern,
    bool IsMetro,
    decimal? Latitude,
    decimal? Longitude,
    bool HasCoordinates,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);
