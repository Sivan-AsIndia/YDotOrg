using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Commands
// =====================================================================================

/// <summary>
/// Creating a time zone.
///
/// <c>TimeZoneKey</c> is the IANA identifier as written — <c>Asia/Kolkata</c>. The stored
/// Code is derived from it by folding the slash to an underscore, so the caller never has to
/// know about the platform-wide code format.
/// </summary>
public sealed record CreateTimeZoneRequest(
    string TimeZoneKey,
    string DisplayName,
    int StandardUtcOffsetMinutes,
    string? ShortName = null,
    bool SupportsDaylightSaving = false,
    string? DaylightSavingRuleNote = null,
    bool IsDefaultRecommended = false,
    MasterDataStatus Status = MasterDataStatus.Active,
    int SortOrder = 0,
    string? Notes = null);

/// <summary>
/// Editing a time zone.
///
/// <c>TimeZoneKey</c> is absent: the IANA key identifies the zone, and repointing it would
/// change what every timestamp already stamped with it means.
/// </summary>
public sealed record UpdateTimeZoneRequest(
    long ExpectedVersion,
    string? DisplayName = null,
    string? ShortName = null,
    int? StandardUtcOffsetMinutes = null,
    bool? SupportsDaylightSaving = null,
    string? DaylightSavingRuleNote = null,
    bool? IsDefaultRecommended = null,
    int? SortOrder = null,
    string? Notes = null);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the time-zone grid.</summary>
public sealed record TimeZoneListItemResponse(
    Guid Id,
    Guid? TenantId,
    string TimeZoneKey,
    string DisplayName,
    string? ShortName,
    int StandardUtcOffsetMinutes,

    /// <summary>The offset written the way a person reads it: "+05:30".</summary>
    string OffsetDisplay,

    bool SupportsDaylightSaving,
    bool IsDefaultRecommended,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>The full time-zone record.</summary>
public sealed record TimeZoneDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string TimeZoneKey,
    string DisplayName,
    string? ShortName,
    int StandardUtcOffsetMinutes,
    string OffsetDisplay,
    bool SupportsDaylightSaving,
    string? DaylightSavingRuleNote,
    bool IsDefaultRecommended,
    MasterDataStatus Status,
    string StatusDescription,
    bool IsActive,
    bool IsPlatformRow,
    int SortOrder,
    string? Notes,

    /// <summary>States that default to this zone. Blocks deletion when non-zero.</summary>
    int StateUsageCount,

    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);
