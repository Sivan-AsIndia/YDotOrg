using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>Creating a currency.</summary>
public sealed record CreateCurrencyRequest(
    string CurrencyCode,
    string CurrencyName,
    int? NumericCode = null,
    CurrencyType CurrencyType = CurrencyType.Fiat,
    string? Symbol = null,
    SymbolPosition SymbolPosition = SymbolPosition.Prefix,
    string? DisplayFormat = null,
    int DecimalPlaces = 2,
    string? MinorUnitName = null,
    RoundingMode RoundingMode = RoundingMode.HalfUp,
    decimal? RoundingStep = null,
    MasterDataStatus Status = MasterDataStatus.Active,
    int SortOrder = 0,
    string? Notes = null);

/// <summary>
/// Editing a currency.
///
/// <c>CurrencyCode</c> is absent: the code IS the currency, and changing INR to USD on an
/// existing row would silently redenominate every donation that referenced it.
/// </summary>
public sealed record UpdateCurrencyRequest(
    long ExpectedVersion,
    string? CurrencyName = null,
    int? NumericCode = null,
    CurrencyType? CurrencyType = null,
    string? Symbol = null,
    SymbolPosition? SymbolPosition = null,
    string? DisplayFormat = null,
    int? DecimalPlaces = null,
    string? MinorUnitName = null,
    RoundingMode? RoundingMode = null,
    decimal? RoundingStep = null,
    int? SortOrder = null,
    string? Notes = null,

    /// <summary>Removes the rounding step, so amounts round to one minor unit again.</summary>
    bool ClearRoundingStep = false);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the currency grid.</summary>
public sealed record CurrencyListItemResponse(
    Guid Id,
    Guid? TenantId,
    string CurrencyCode,
    string CurrencyName,
    int? NumericCode,
    CurrencyType CurrencyType,
    string? Symbol,
    SymbolPosition SymbolPosition,
    int DecimalPlaces,

    /// <summary>A worked example of the format, so the grid shows the effect rather than the rule.</summary>
    string SampleAmount,

    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full currency record.</summary>
public sealed record CurrencyDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string CurrencyCode,
    string CurrencyName,
    int? NumericCode,
    CurrencyType CurrencyType,
    string? Symbol,
    SymbolPosition SymbolPosition,
    string? DisplayFormat,
    int DecimalPlaces,
    string? MinorUnitName,
    RoundingMode RoundingMode,
    decimal? RoundingStep,
    bool IsZeroDecimal,
    string SampleAmount,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    string? Notes,

    /// <summary>Countries that name this currency as their default. Blocks deletion when non-zero.</summary>
    int CountryUsageCount,

    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);
