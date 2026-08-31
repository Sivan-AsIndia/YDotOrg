using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>
/// Manual mapping for the Countries slice: request to entity, entity to response.
///
/// THE MAPPER IS WHERE NORMALISATION HAPPENS, not the handler and not the validator. A
/// validator answers "is this acceptable?" and a handler answers "is this allowed?"; turning
/// " in " into "IN" is neither, and doing it here means the seeder, an import and the create
/// screen all produce the same bytes in the column.
/// </summary>
public static class CountryMappingConfig
{
    /// <summary>
    /// Builds a new Country from a create request.
    ///
    /// <paramref name="tenantId"/> is the scope the row is being written into: null for a
    /// SuperAdmin adding to the shared catalogue, the Organisation for a Tenant adding one of
    /// its own. It is passed in rather than read from the request for the usual reason - a
    /// caller must never choose which Organisation a row lands in.
    ///
    /// Ids, audit columns and the version are NOT set here. <c>BaseEntity</c> supplies the
    /// Guid and <c>IamDbContext.SaveChangesAsync</c> stamps the rest, so a mapper that also
    /// set them would be writing values that are about to be overwritten.
    /// </summary>
    public static Country ToEntity(this CreateCountryRequest request, Guid? tenantId, Guid businessUnitId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Country
        {
            TenantId = tenantId,
            BusinessUnitId = businessUnitId,
            Code = CodeValue.Parse(request.CountryCode).Value,
            Name = request.CountryName.Trim(),
            OfficialName = Clean(request.OfficialName),
            Region = request.Region,
            Iso2 = IsoAlpha2Value.Parse(request.Iso2).Value,
            Iso3 = Clean(request.Iso3)?.ToUpperInvariant(),
            NumericCode = Clean(request.NumericCode),
            DefaultCurrencyCode = CurrencyCodeValue.TryParse(request.DefaultCurrencyCode)?.Value,
            HasStates = request.HasStates,
            PostalCodePattern = Clean(request.PostalCodePattern),
            PhoneCountryCode = Clean(request.PhoneCountryCode),
            Status = request.Status,
            SortOrder = request.SortOrder,
            Notes = Clean(request.Notes)
        };
    }

    /// <summary>
    /// Applies an update in place.
    ///
    /// EVERY FIELD IS "NULL MEANS LEAVE IT ALONE", which is what lets the detail screen send
    /// only what was touched. The two fields where null is a meaningful value in its own
    /// right - Region and DefaultCurrencyCode - therefore cannot be cleared through this
    /// path, and that is a deliberate trade: silently clearing a country's currency because a
    /// partial update omitted it would be far worse than needing a separate action to do it
    /// on purpose.
    /// </summary>
    public static void ApplyTo(this UpdateCountryRequest request, Country country)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(country);

        if (!string.IsNullOrWhiteSpace(request.CountryName))
        {
            country.Name = request.CountryName.Trim();
        }

        if (request.OfficialName is not null)
        {
            country.OfficialName = Clean(request.OfficialName);
        }

        if (request.Region.HasValue)
        {
            country.Region = request.Region;
        }

        if (!string.IsNullOrWhiteSpace(request.Iso2))
        {
            country.Iso2 = IsoAlpha2Value.Parse(request.Iso2).Value;
        }

        if (request.Iso3 is not null)
        {
            country.Iso3 = Clean(request.Iso3)?.ToUpperInvariant();
        }

        if (request.NumericCode is not null)
        {
            country.NumericCode = Clean(request.NumericCode);
        }

        if (request.DefaultCurrencyCode is not null)
        {
            country.DefaultCurrencyCode = CurrencyCodeValue.TryParse(request.DefaultCurrencyCode)?.Value;
        }

        if (request.HasStates.HasValue)
        {
            country.HasStates = request.HasStates.Value;
        }

        if (request.PostalCodePattern is not null)
        {
            country.PostalCodePattern = Clean(request.PostalCodePattern);
        }

        if (request.PhoneCountryCode is not null)
        {
            country.PhoneCountryCode = Clean(request.PhoneCountryCode);
        }

        if (request.SortOrder.HasValue)
        {
            country.SortOrder = request.SortOrder.Value;
        }

        if (request.Notes is not null)
        {
            country.Notes = Clean(request.Notes);
        }
    }

    /// <summary>One row of the grid.</summary>
    public static CountryListItemResponse ToListItemResponse(this Country country, int stateProvinceCount)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new CountryListItemResponse(
            country.Id,
            country.TenantId,
            country.Code,
            country.Name,
            country.OfficialName,
            country.Region,
            country.Iso2,
            country.Iso3,
            FlagFor(country.Iso2),
            country.DefaultCurrencyCode,
            country.PhoneCountryCode,
            country.HasStates,
            country.Status,
            GlobalMasterMappingConfig.DescribeStatus(country.Status),
            GlobalMasterMappingConfig.IsActiveFlag(country.Status),
            country.IsPlatformRow,
            country.SortOrder,
            stateProvinceCount,
            country.UpdatedAtUtc,
            country.Version);
    }

    /// <summary>The detail panel.</summary>
    public static CountryDetailResponse ToDetailResponse(
        this Country country, int stateProvinceCount, int cityCount, bool isSuperAdmin)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new CountryDetailResponse(
            country.Id,
            country.TenantId,
            country.BusinessUnitId,
            country.Code,
            country.Name,
            country.OfficialName,
            country.Region,
            country.Iso2,
            country.Iso3,
            country.NumericCode,
            FlagFor(country.Iso2),
            country.DefaultCurrencyCode,
            country.HasStates,
            country.PostalCodePattern,
            country.PhoneCountryCode,
            country.Status,
            GlobalMasterMappingConfig.DescribeStatus(country.Status),
            GlobalMasterMappingConfig.IsActiveFlag(country.Status),
            country.IsPlatformRow,
            country.SortOrder,
            country.Notes,
            stateProvinceCount,
            cityCount,
            country.CreatedAtUtc,
            country.CreatedByUserId,
            country.UpdatedAtUtc,
            country.UpdatedByUserId,
            country.Version,
            GlobalMasterMappingConfig.PermittedActionsFor(
                country, isSuperAdmin, stateProvinceCount + cityCount));
    }

    /// <summary>One option in a country picker.</summary>
    public static MasterLookupResponse ToLookupResponse(this Country country)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new MasterLookupResponse(
            country.Id, country.Code, country.Name, country.Status, country.IsPlatformRow, country.SortOrder);
    }

    /// <summary>One line of the CSV export.</summary>
    public static CountryExportRow ToExportRow(this Country country)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new CountryExportRow(
            country.Code,
            country.Name,
            country.OfficialName,
            country.Region?.ToString(),
            country.Iso2,
            country.Iso3,
            country.NumericCode,
            country.DefaultCurrencyCode,
            country.HasStates ? "Yes" : "No",
            country.PhoneCountryCode,
            country.Status.ToString(),
            GlobalMasterMappingConfig.DescribeScope(country.IsPlatformRow),
            country.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The flag emoji for an alpha-2 code, or an empty string when the code is unusable.
    ///
    /// Served from here rather than computed in the browser because the Angular country grid
    /// already had its own copy of this mapping, and two copies of one rule is one too many.
    /// </summary>
    public static string FlagFor(string? iso2) =>
        IsoAlpha2Value.TryParse(iso2)?.ToFlagEmoji() ?? string.Empty;

    /// <summary>Trims, and turns an all-whitespace string into a null so the column stays clean.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
