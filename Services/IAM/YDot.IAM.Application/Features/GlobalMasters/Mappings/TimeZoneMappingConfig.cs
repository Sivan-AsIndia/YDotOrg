using System.Globalization;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>Manual mapping for the Time Zones slice.</summary>
public static class TimeZoneMappingConfig
{
    /// <summary>
    /// Builds a new TimeZoneDefinition.
    ///
    /// The stored Code is DERIVED from the IANA key rather than asked for separately: the two
    /// would only ever be allowed to disagree by accident, and asking an operator to type
    /// <c>ASIA_KOLKATA</c> beside <c>Asia/Kolkata</c> is asking for a typo that makes the two
    /// permanently different.
    /// </summary>
    public static TimeZoneDefinition ToEntity(
        this CreateTimeZoneRequest request, Guid? tenantId, Guid businessUnitId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ianaKey = request.TimeZoneKey.Trim();

        return new TimeZoneDefinition
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            Code = ToCode(ianaKey),
            IanaKey = ianaKey,
            Name = request.DisplayName.Trim(),
            ShortName = Clean(request.ShortName)?.ToUpperInvariant(),
            StandardUtcOffsetMinutes = request.StandardUtcOffsetMinutes,
            SupportsDaylightSaving = request.SupportsDaylightSaving,
            DaylightSavingRuleNote = Clean(request.DaylightSavingRuleNote),
            IsDefaultRecommended = request.IsDefaultRecommended,
            Status = request.Status,
            SortOrder = request.SortOrder,
            Notes = Clean(request.Notes)
        };
    }

    /// <summary>Applies an update in place. Null means "leave it alone".</summary>
    public static void ApplyTo(this UpdateTimeZoneRequest request, TimeZoneDefinition timeZone)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            timeZone.Name = request.DisplayName.Trim();
        }

        if (request.ShortName is not null)
        {
            timeZone.ShortName = Clean(request.ShortName)?.ToUpperInvariant();
        }

        if (request.StandardUtcOffsetMinutes.HasValue)
        {
            timeZone.StandardUtcOffsetMinutes = request.StandardUtcOffsetMinutes.Value;
        }

        if (request.SupportsDaylightSaving.HasValue)
        {
            timeZone.SupportsDaylightSaving = request.SupportsDaylightSaving.Value;

            // A zone that no longer observes daylight saving has no rule to describe, so the
            // note goes with it rather than lingering to contradict the flag beside it.
            if (!timeZone.SupportsDaylightSaving)
            {
                timeZone.DaylightSavingRuleNote = null;
            }
        }

        if (request.DaylightSavingRuleNote is not null && timeZone.SupportsDaylightSaving)
        {
            timeZone.DaylightSavingRuleNote = Clean(request.DaylightSavingRuleNote);
        }

        if (request.IsDefaultRecommended.HasValue)
        {
            timeZone.IsDefaultRecommended = request.IsDefaultRecommended.Value;
        }

        if (request.SortOrder.HasValue)
        {
            timeZone.SortOrder = request.SortOrder.Value;
        }

        if (request.Notes is not null)
        {
            timeZone.Notes = Clean(request.Notes);
        }
    }

    /// <summary>One row of the grid.</summary>
    public static TimeZoneListItemResponse ToListItemResponse(this TimeZoneDefinition timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new TimeZoneListItemResponse(
            timeZone.Id,
            timeZone.TenantId,
            timeZone.IanaKey,
            timeZone.Name,
            timeZone.ShortName,
            timeZone.StandardUtcOffsetMinutes,
            timeZone.OffsetDisplay,
            timeZone.SupportsDaylightSaving,
            timeZone.IsDefaultRecommended,
            timeZone.Status,
            GlobalMasterMappingConfig.DescribeStatus(timeZone.Status),
            GlobalMasterMappingConfig.IsActiveFlag(timeZone.Status),
            timeZone.IsPlatformRow,
            timeZone.SortOrder,
            timeZone.UpdatedAtUtc,
            timeZone.Version);
    }

    /// <summary>The detail panel.</summary>
    public static TimeZoneDetailResponse ToDetailResponse(
        this TimeZoneDefinition timeZone, int stateUsageCount, bool isSuperAdmin)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new TimeZoneDetailResponse(
            timeZone.Id,
            timeZone.TenantId,
            timeZone.BusinessUnitId,
            timeZone.IanaKey,
            timeZone.Name,
            timeZone.ShortName,
            timeZone.StandardUtcOffsetMinutes,
            timeZone.OffsetDisplay,
            timeZone.SupportsDaylightSaving,
            timeZone.DaylightSavingRuleNote,
            timeZone.IsDefaultRecommended,
            timeZone.Status,
            GlobalMasterMappingConfig.DescribeStatus(timeZone.Status),
            GlobalMasterMappingConfig.IsActiveFlag(timeZone.Status),
            timeZone.IsPlatformRow,
            timeZone.SortOrder,
            timeZone.Notes,
            stateUsageCount,
            timeZone.CreatedAtUtc,
            timeZone.CreatedByUserId,
            timeZone.UpdatedAtUtc,
            timeZone.UpdatedByUserId,
            timeZone.Version,
            GlobalMasterMappingConfig.PermittedActionsFor(timeZone, isSuperAdmin, stateUsageCount));
    }

    /// <summary>
    /// One option in a time-zone picker.
    ///
    /// The label leads with the offset - "(+05:30) India Standard Time" - because a list of
    /// four hundred zone names sorted alphabetically is unusable, and the offset is what an
    /// operator is actually looking for.
    /// </summary>
    public static MasterLookupResponse ToLookupResponse(this TimeZoneDefinition timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new MasterLookupResponse(
            timeZone.Id,
            timeZone.Code,
            $"({timeZone.OffsetDisplay}) {timeZone.Name}",
            timeZone.Status,
            timeZone.IsPlatformRow,
            timeZone.SortOrder);
    }

    /// <summary>One line of the CSV export.</summary>
    public static TimeZoneExportRow ToExportRow(this TimeZoneDefinition timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new TimeZoneExportRow(
            timeZone.IanaKey,
            timeZone.Name,
            timeZone.ShortName,
            timeZone.OffsetDisplay,
            timeZone.StandardUtcOffsetMinutes.ToString(CultureInfo.InvariantCulture),
            timeZone.SupportsDaylightSaving ? "Yes" : "No",
            timeZone.IsDefaultRecommended ? "Yes" : "No",
            timeZone.Status.ToString(),
            GlobalMasterMappingConfig.DescribeScope(timeZone.IsPlatformRow),
            timeZone.SortOrder.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <c>Asia/Kolkata</c> becomes <c>ASIA_KOLKATA</c>.
    ///
    /// Not <c>CodeValue.FromName</c>, which would also collapse the runs of underscores that
    /// a zone name legitimately contains - <c>America/Argentina/Rio_Gallegos</c> has to stay
    /// distinguishable from a hypothetical <c>Rio-Gallegos</c>. Only the separators are
    /// folded here, and nothing else about the key is altered.
    /// </summary>
    public static string ToCode(string ianaKey)
    {
        ArgumentNullException.ThrowIfNull(ianaKey);

        return ianaKey.Trim()
            .ToUpperInvariant()
            .Replace('/', '_')
            .Replace(' ', '_');
    }

    /// <summary>
    /// One option in an address form's time-zone picker.
    ///
    /// DISTINCT FROM <see cref="ToLookupResponse"/>, which collapses the zone to a code and a
    /// label. A form outside the Masters screens needs more than a label: the IANA key to send
    /// onward to anything that actually converts a time, the offset to sort by, and the
    /// short name to render compactly beside a date.
    ///
    /// <paramref name="isPrimaryForCountry"/> is passed in rather than read off the zone
    /// because it is not a property OF the zone - the same zone is primary for one country and
    /// merely available in the next.
    /// </summary>
    public static TimeZoneLookupResponse ToGeoLookupResponse(
        this TimeZoneDefinition timeZone, bool isPrimaryForCountry)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new TimeZoneLookupResponse(
            timeZone.Id,
            timeZone.Code,
            timeZone.IanaKey,
            $"({timeZone.OffsetDisplay}) {timeZone.Name}",
            timeZone.ShortName,
            timeZone.OffsetDisplay,
            timeZone.StandardUtcOffsetMinutes,
            timeZone.SupportsDaylightSaving,
            isPrimaryForCountry,
            timeZone.IsDefaultRecommended,
            timeZone.Status,
            timeZone.IsPlatformRow,
            timeZone.SortOrder);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
