using System.Globalization;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>Manual mapping for the States and Provinces slice.</summary>
public static class StateProvinceMappingConfig
{
    /// <summary>
    /// Builds a new StateProvince.
    ///
    /// <paramref name="country"/> is the LOADED parent rather than the id off the request, so
    /// the row can only ever be attached to a country the caller was actually able to read -
    /// which, under the scoped query filter, means the platform catalogue or their own.
    /// </summary>
    public static StateProvince ToEntity(
        this CreateStateProvinceRequest request, Country country, Guid? tenantId, Guid businessUnitId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(country);

        return new StateProvince
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            Code = CodeValue.Parse(request.StateProvinceCode).Value,
            Name = request.StateProvinceName.Trim(),
            DisplayName = Clean(request.DisplayName),
            CountryId = country.Id,
            JurisdictionType = request.JurisdictionType,

            // Only meaningful for Other. Cleared otherwise so a jurisdiction changed away from
            // Other does not leave a stale description behind it.
            OtherJurisdictionType = request.JurisdictionType == JurisdictionType.Other
                ? Clean(request.OtherJurisdictionType)
                : null,

            IsFederalJurisdiction = request.IsFederalJurisdiction,
            GstStateCode = Clean(request.GstStateCode),
            StateTaxJurisdictionCode = Clean(request.StateTaxJurisdictionCode),
            DefaultTimeZoneId = request.DefaultTimeZoneId,
            PostalCodePattern = Clean(request.PostalCodePattern),
            AddressFormatHint = Clean(request.AddressFormatHint),
            Status = request.Status,
            SortOrder = request.SortOrder,
            Notes = Clean(request.Notes)
        };
    }

    /// <summary>Applies an update in place. Null means "leave it alone".</summary>
    public static void ApplyTo(this UpdateStateProvinceRequest request, StateProvince state)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        if (!string.IsNullOrWhiteSpace(request.StateProvinceName))
        {
            state.Name = request.StateProvinceName.Trim();
        }

        if (request.DisplayName is not null)
        {
            state.DisplayName = Clean(request.DisplayName);
        }

        if (request.JurisdictionType.HasValue)
        {
            state.JurisdictionType = request.JurisdictionType.Value;

            // Moving away from Other clears the free-text description, so it cannot linger and
            // contradict the enum beside it.
            if (state.JurisdictionType != JurisdictionType.Other)
            {
                state.OtherJurisdictionType = null;
            }
        }

        if (request.OtherJurisdictionType is not null
            && state.JurisdictionType == JurisdictionType.Other)
        {
            state.OtherJurisdictionType = Clean(request.OtherJurisdictionType);
        }

        if (request.IsFederalJurisdiction.HasValue)
        {
            state.IsFederalJurisdiction = request.IsFederalJurisdiction.Value;
        }

        if (request.GstStateCode is not null)
        {
            state.GstStateCode = Clean(request.GstStateCode);
        }

        if (request.StateTaxJurisdictionCode is not null)
        {
            state.StateTaxJurisdictionCode = Clean(request.StateTaxJurisdictionCode);
        }

        if (request.DefaultTimeZoneId.HasValue)
        {
            state.DefaultTimeZoneId = request.DefaultTimeZoneId;
        }

        if (request.PostalCodePattern is not null)
        {
            state.PostalCodePattern = Clean(request.PostalCodePattern);
        }

        if (request.AddressFormatHint is not null)
        {
            state.AddressFormatHint = Clean(request.AddressFormatHint);
        }

        if (request.SortOrder.HasValue)
        {
            state.SortOrder = request.SortOrder.Value;
        }

        if (request.Notes is not null)
        {
            state.Notes = Clean(request.Notes);
        }
    }

    /// <summary>One row of the grid. The country is passed in because the grid shows its name.</summary>
    public static StateProvinceListItemResponse ToListItemResponse(
        this StateProvince state, string countryCode, string countryName, int cityCount)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new StateProvinceListItemResponse(
            state.Id,
            state.TenantId,
            state.Code,
            state.Name,
            state.DisplayName,
            state.CountryId,
            countryCode,
            countryName,
            state.JurisdictionType,
            DescribeJurisdiction(state),
            state.IsFederalJurisdiction,
            state.GstStateCode,
            state.Status,
            GlobalMasterMappingConfig.DescribeStatus(state.Status),
            GlobalMasterMappingConfig.IsActiveFlag(state.Status),
            state.IsPlatformRow,
            state.SortOrder,
            cityCount,
            state.UpdatedAtUtc,
            state.Version);
    }

    /// <summary>The detail panel.</summary>
    public static StateProvinceDetailResponse ToDetailResponse(
        this StateProvince state,
        string countryCode,
        string countryName,
        string? defaultTimeZoneName,
        int cityCount,
        bool isSuperAdmin)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new StateProvinceDetailResponse(
            state.Id,
            state.TenantId,
            state.BusinessUnitId,
            state.Code,
            state.Name,
            state.DisplayName,
            state.CountryId,
            countryCode,
            countryName,
            state.JurisdictionType,
            DescribeJurisdiction(state),
            state.OtherJurisdictionType,
            state.IsFederalJurisdiction,
            state.GstStateCode,
            state.StateTaxJurisdictionCode,
            state.DefaultTimeZoneId,
            defaultTimeZoneName,
            state.PostalCodePattern,
            state.AddressFormatHint,
            state.Status,
            GlobalMasterMappingConfig.DescribeStatus(state.Status),
            GlobalMasterMappingConfig.IsActiveFlag(state.Status),
            state.IsPlatformRow,
            state.SortOrder,
            state.Notes,
            cityCount,
            state.CreatedAtUtc,
            state.CreatedByUserId,
            state.UpdatedAtUtc,
            state.UpdatedByUserId,
            state.Version,
            GlobalMasterMappingConfig.PermittedActionsFor(state, isSuperAdmin, cityCount));
    }

    /// <summary>One option in a state picker.</summary>
    public static MasterLookupResponse ToLookupResponse(this StateProvince state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new MasterLookupResponse(
            state.Id,
            state.Code,
            state.DisplayName ?? state.Name,
            state.Status,
            state.IsPlatformRow,
            state.SortOrder);
    }

    /// <summary>One line of the CSV export.</summary>
    public static StateProvinceExportRow ToExportRow(
        this StateProvince state, string countryCode, string countryName, string? defaultTimeZoneName)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new StateProvinceExportRow(
            state.Code,
            state.Name,
            countryCode,
            countryName,
            DescribeJurisdiction(state),
            state.IsFederalJurisdiction ? "Yes" : "No",
            state.GstStateCode,
            defaultTimeZoneName,
            state.Status.ToString(),
            GlobalMasterMappingConfig.DescribeScope(state.IsPlatformRow),
            state.SortOrder.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// What to call the jurisdiction on screen.
    ///
    /// <c>Other</c> shows the free-text description the operator typed rather than the word
    /// "Other", which tells a reader nothing.
    /// </summary>
    public static string DescribeJurisdiction(StateProvince state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.JurisdictionType == JurisdictionType.Other
            && !string.IsNullOrWhiteSpace(state.OtherJurisdictionType))
        {
            return state.OtherJurisdictionType;
        }

        return state.JurisdictionType switch
        {
            JurisdictionType.UnionTerritory => "Union Territory",
            _ => state.JurisdictionType.ToString()
        };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
