using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>Creating a state, province or union territory beneath a country.</summary>
public sealed record CreateStateProvinceRequest(
    string StateProvinceCode,
    string StateProvinceName,
    Guid CountryId,
    string? DisplayName = null,
    JurisdictionType JurisdictionType = JurisdictionType.State,
    string? OtherJurisdictionType = null,
    bool IsFederalJurisdiction = false,
    string? GstStateCode = null,
    string? StateTaxJurisdictionCode = null,
    Guid? DefaultTimeZoneId = null,
    string? PostalCodePattern = null,
    string? AddressFormatHint = null,
    MasterDataStatus Status = MasterDataStatus.Active,
    int SortOrder = 0,
    string? Notes = null);

/// <summary>
/// Editing a state.
///
/// <c>CountryId</c> is ABSENT ON PURPOSE, and it is the one field a reader is most likely to
/// expect. Moving a state to a different country would silently re-parent every city beneath
/// it and invalidate every address that referenced it. Delete and recreate is the honest
/// operation, and it is one the dependency check will refuse while anything still points at
/// the row - which is exactly the conversation that ought to happen.
/// </summary>
public sealed record UpdateStateProvinceRequest(
    long ExpectedVersion,
    string? StateProvinceName = null,
    string? DisplayName = null,
    JurisdictionType? JurisdictionType = null,
    string? OtherJurisdictionType = null,
    bool? IsFederalJurisdiction = null,
    string? GstStateCode = null,
    string? StateTaxJurisdictionCode = null,
    Guid? DefaultTimeZoneId = null,
    string? PostalCodePattern = null,
    string? AddressFormatHint = null,
    int? SortOrder = null,
    string? Notes = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the state grid.</summary>
public sealed record StateProvinceListItemResponse(
    Guid Id,
    Guid? TenantId,
    string StateProvinceCode,
    string StateProvinceName,
    string? DisplayName,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    JurisdictionType JurisdictionType,
    string JurisdictionDescription,
    bool IsFederalJurisdiction,
    string? GstStateCode,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    int CityCount,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full state record.</summary>
public sealed record StateProvinceDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string StateProvinceCode,
    string StateProvinceName,
    string? DisplayName,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    JurisdictionType JurisdictionType,
    string JurisdictionDescription,
    string? OtherJurisdictionType,
    bool IsFederalJurisdiction,
    string? GstStateCode,
    string? StateTaxJurisdictionCode,
    Guid? DefaultTimeZoneId,
    string? DefaultTimeZoneName,
    string? PostalCodePattern,
    string? AddressFormatHint,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    string? Notes,
    int CityCount,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);
