using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Application.Features.ReferenceData.Queries;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the five Masters grids, their detail panels and their pickers.
///
/// THE SCOPE FILTER IS ALREADY APPLIED UNDERNEATH. Every query in this file runs against a
/// DbSet the global filter has narrowed to "platform rows OR mine", so nothing here has to
/// remember a Tenant predicate and nothing here CAN reach across an Organisation boundary.
/// What <see cref="ApplyScope"/> adds is the opposite: a way for the SCREEN to narrow that
/// view further, to just the platform rows or just its own.
///
/// COUNTS ARE PROJECTED, NOT FETCHED PER ROW. A country list showing "28 states" for twenty
/// countries is one query with a correlated subquery, not twenty-one queries - EF turns
/// <c>country.StateProvinces.Count()</c> inside a Select into exactly that.
/// </summary>
public sealed class GlobalMasterReadService(
    IamDbContext context,
    ICurrentUser currentUser) : IGlobalMasterReadService
{
    /// <summary>The most rows any picker returns. Enough for every ISO list with room to spare.</summary>
    private const int LookupLimit = 1000;

    // =====================================================================================
    // Countries
    // =====================================================================================

    public async Task<PagedResponse<CountryListItemResponse>> SearchCountriesAsync(
        CountrySearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyScope(context.Countries.AsNoTracking(), filter);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(country =>
                country.Name.ToLower().Contains(term)
                || country.Code.ToLower().Contains(term)
                || country.Iso2.ToLower().Contains(term)
                || (country.Iso3 != null && country.Iso3.ToLower().Contains(term))
                || (country.OfficialName != null && country.OfficialName.ToLower().Contains(term)));
        }

        if (filter.Region.HasValue)
        {
            query = query.Where(country => country.Region == filter.Region.Value);
        }

        if (filter.HasStates.HasValue)
        {
            query = query.Where(country => country.HasStates == filter.HasStates.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.DefaultCurrencyCode))
        {
            var currencyCode = filter.DefaultCurrencyCode.Trim().ToUpperInvariant();
            query = query.Where(country => country.DefaultCurrencyCode == currencyCode);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await SortMasters(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(country => new
            {
                Country = country,
                StateCount = country.StateProvinces.Count()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => row.Country.ToListItemResponse(row.StateCount))
            .ToList();

        return new PagedResponse<CountryListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<CountryDetailResponse?> GetCountryDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await context.Countries
            .AsNoTracking()
            .Where(country => country.Id == id)
            .Select(country => new
            {
                Country = country,
                StateCount = country.StateProvinces.Count(),
                CityCount = country.Cities.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row?.Country.ToDetailResponse(row.StateCount, row.CityCount, currentUser.IsSuperAdmin);
    }

    public async Task<IReadOnlyList<CountryExportRow>> GetCountryExportRowsAsync(
        CountrySearchFilter filter, CancellationToken cancellationToken)
    {
        // Reuses the grid query so the export and the screen can never disagree about what the
        // filter meant - which is the failure the two-implementations approach always produces
        // eventually.
        var page = await SearchCountriesAsync(filter, cancellationToken);

        var ids = page.Items.Select(item => item.Id).ToList();

        var countries = await context.Countries
            .AsNoTracking()
            .Where(country => ids.Contains(country.Id))
            .ToListAsync(cancellationToken);

        // Re-ordered to match the page, because the IN clause above does not preserve order
        // and an export whose rows are shuffled relative to the screen is confusing to check.
        return [.. ids
            .Select(id => countries.FirstOrDefault(country => country.Id == id))
            .Where(country => country is not null)
            .Select(country => country!.ToExportRow())];
    }

    // =====================================================================================
    // States and provinces
    // =====================================================================================

    public async Task<PagedResponse<StateProvinceListItemResponse>> SearchStateProvincesAsync(
        StateProvinceSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyScope(context.StateProvinces.AsNoTracking(), filter);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(state =>
                state.Name.ToLower().Contains(term)
                || state.Code.ToLower().Contains(term)
                || (state.DisplayName != null && state.DisplayName.ToLower().Contains(term))
                || (state.GstStateCode != null && state.GstStateCode.Contains(term)));
        }

        if (filter.CountryId.HasValue)
        {
            query = query.Where(state => state.CountryId == filter.CountryId.Value);
        }

        if (filter.JurisdictionType.HasValue)
        {
            query = query.Where(state => state.JurisdictionType == filter.JurisdictionType.Value);
        }

        if (filter.IsFederalJurisdiction.HasValue)
        {
            query = query.Where(state => state.IsFederalJurisdiction == filter.IsFederalJurisdiction.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await SortMasters(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(state => new
            {
                State = state,
                CountryCode = state.Country.Code,
                CountryName = state.Country.Name,
                CityCount = state.Cities.Count()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => row.State.ToListItemResponse(row.CountryCode, row.CountryName, row.CityCount))
            .ToList();

        return new PagedResponse<StateProvinceListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<StateProvinceDetailResponse?> GetStateProvinceDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await context.StateProvinces
            .AsNoTracking()
            .Where(state => state.Id == id)
            .Select(state => new
            {
                State = state,
                CountryCode = state.Country.Code,
                CountryName = state.Country.Name,
                TimeZoneName = state.DefaultTimeZone != null ? state.DefaultTimeZone.Name : null,
                CityCount = state.Cities.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row?.State.ToDetailResponse(
            row.CountryCode, row.CountryName, row.TimeZoneName, row.CityCount, currentUser.IsSuperAdmin);
    }

    public async Task<IReadOnlyList<StateProvinceExportRow>> GetStateProvinceExportRowsAsync(
        StateProvinceSearchFilter filter, CancellationToken cancellationToken)
    {
        var page = await SearchStateProvincesAsync(filter, cancellationToken);

        var ids = page.Items.Select(item => item.Id).ToList();

        var rows = await context.StateProvinces
            .AsNoTracking()
            .Where(state => ids.Contains(state.Id))
            .Select(state => new
            {
                State = state,
                CountryCode = state.Country.Code,
                CountryName = state.Country.Name,
                TimeZoneName = state.DefaultTimeZone != null ? state.DefaultTimeZone.Name : null
            })
            .ToListAsync(cancellationToken);

        return [.. ids
            .Select(id => rows.FirstOrDefault(row => row.State.Id == id))
            .Where(row => row is not null)
            .Select(row => row!.State.ToExportRow(row.CountryCode, row.CountryName, row.TimeZoneName))];
    }

    // =====================================================================================
    // Cities
    // =====================================================================================

    public async Task<PagedResponse<CityListItemResponse>> SearchCitiesAsync(
        CitySearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyScope(context.Cities.AsNoTracking(), filter);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(city =>
                city.Name.ToLower().Contains(term)
                || city.Code.ToLower().Contains(term)
                || (city.DisplayName != null && city.DisplayName.ToLower().Contains(term)));
        }

        if (filter.CountryId.HasValue)
        {
            query = query.Where(city => city.CountryId == filter.CountryId.Value);
        }

        if (filter.StateProvinceId.HasValue)
        {
            query = query.Where(city => city.StateProvinceId == filter.StateProvinceId.Value);
        }

        if (filter.IsMetro.HasValue)
        {
            query = query.Where(city => city.IsMetro == filter.IsMetro.Value);
        }

        // "Show me what still needs geocoding" - the reason this filter exists rather than
        // leaving an operator to page through looking for blanks.
        if (filter.HasCoordinates.HasValue)
        {
            query = filter.HasCoordinates.Value
                ? query.Where(city => city.Latitude != null)
                : query.Where(city => city.Latitude == null);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await SortMasters(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(city => new
            {
                City = city,
                StateCode = city.StateProvince.Code,
                StateName = city.StateProvince.Name,
                CountryCode = city.Country.Code,
                CountryName = city.Country.Name
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => row.City.ToListItemResponse(
                row.StateCode, row.StateName, row.CountryCode, row.CountryName))
            .ToList();

        return new PagedResponse<CityListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<CityDetailResponse?> GetCityDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.Cities
            .AsNoTracking()
            .Where(city => city.Id == id)
            .Select(city => new
            {
                City = city,
                StateCode = city.StateProvince.Code,
                StateName = city.StateProvince.Name,
                CountryCode = city.Country.Code,
                CountryName = city.Country.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row?.City.ToDetailResponse(
            row.StateCode, row.StateName, row.CountryCode, row.CountryName, currentUser.IsSuperAdmin);
    }

    public async Task<IReadOnlyList<CityExportRow>> GetCityExportRowsAsync(
        CitySearchFilter filter, CancellationToken cancellationToken)
    {
        var page = await SearchCitiesAsync(filter, cancellationToken);

        var ids = page.Items.Select(item => item.Id).ToList();

        var rows = await context.Cities
            .AsNoTracking()
            .Where(city => ids.Contains(city.Id))
            .Select(city => new
            {
                City = city,
                StateCode = city.StateProvince.Code,
                StateName = city.StateProvince.Name,
                CountryCode = city.Country.Code,
                CountryName = city.Country.Name
            })
            .ToListAsync(cancellationToken);

        return [.. ids
            .Select(id => rows.FirstOrDefault(row => row.City.Id == id))
            .Where(row => row is not null)
            .Select(row => row!.City.ToExportRow(
                row.StateCode, row.StateName, row.CountryCode, row.CountryName))];
    }

    // =====================================================================================
    // Currencies
    // =====================================================================================

    public async Task<PagedResponse<CurrencyListItemResponse>> SearchCurrenciesAsync(
        CurrencySearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyScope(context.Currencies.AsNoTracking(), filter);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(currency =>
                currency.Name.ToLower().Contains(term)
                || currency.Code.ToLower().Contains(term)
                || (currency.MinorUnitName != null && currency.MinorUnitName.ToLower().Contains(term)));
        }

        if (filter.CurrencyType.HasValue)
        {
            query = query.Where(currency => currency.CurrencyType == filter.CurrencyType.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await SortMasters(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(currency => currency.ToListItemResponse()).ToList();

        return new PagedResponse<CurrencyListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<CurrencyDetailResponse?> GetCurrencyDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var currency = await context.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (currency is null)
        {
            return null;
        }

        // A separate query rather than a projection, because the link is by CODE and not by a
        // navigation property EF could correlate for us. See Country.DefaultCurrencyCode.
        var usageCount = await context.Countries
            .CountAsync(country => country.DefaultCurrencyCode == currency.Code, cancellationToken);

        return currency.ToDetailResponse(usageCount, currentUser.IsSuperAdmin);
    }

    public async Task<IReadOnlyList<CurrencyExportRow>> GetCurrencyExportRowsAsync(
        CurrencySearchFilter filter, CancellationToken cancellationToken)
    {
        var page = await SearchCurrenciesAsync(filter, cancellationToken);

        var ids = page.Items.Select(item => item.Id).ToList();

        var currencies = await context.Currencies
            .AsNoTracking()
            .Where(currency => ids.Contains(currency.Id))
            .ToListAsync(cancellationToken);

        return [.. ids
            .Select(id => currencies.FirstOrDefault(currency => currency.Id == id))
            .Where(currency => currency is not null)
            .Select(currency => currency!.ToExportRow())];
    }

    // =====================================================================================
    // Time zones
    // =====================================================================================

    public async Task<PagedResponse<TimeZoneListItemResponse>> SearchTimeZonesAsync(
        TimeZoneSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyScope(context.TimeZones.AsNoTracking(), filter);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(zone =>
                zone.Name.ToLower().Contains(term)
                || zone.IanaKey.ToLower().Contains(term)
                || (zone.ShortName != null && zone.ShortName.ToLower().Contains(term)));
        }

        if (filter.SupportsDaylightSaving.HasValue)
        {
            query = query.Where(zone => zone.SupportsDaylightSaving == filter.SupportsDaylightSaving.Value);
        }

        if (filter.IsDefaultRecommended.HasValue)
        {
            query = query.Where(zone => zone.IsDefaultRecommended == filter.IsDefaultRecommended.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        // ORDERED BY OFFSET rather than by the shared sort helper, because that is the order a
        // time-zone list is actually read in. A default of SortOrder-then-name would give the
        // alphabetical list nobody can use.
        var rows = string.IsNullOrWhiteSpace(filter.Sort)
            ? await query
                .OrderBy(zone => zone.TenantId != null)
                .ThenBy(zone => zone.StandardUtcOffsetMinutes)
                .ThenBy(zone => zone.Name)
                .Skip(filter.Skip)
                .Take(filter.PageSize)
                .ToListAsync(cancellationToken)
            : await SortMasters(query, filter.Sort)
                .Skip(filter.Skip)
                .Take(filter.PageSize)
                .ToListAsync(cancellationToken);

        var items = rows.Select(zone => zone.ToListItemResponse()).ToList();

        return new PagedResponse<TimeZoneListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<TimeZoneDetailResponse?> GetTimeZoneDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await context.TimeZones
            .AsNoTracking()
            .Where(zone => zone.Id == id)
            .Select(zone => new
            {
                Zone = zone,
                UsageCount = zone.StateProvinces.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row?.Zone.ToDetailResponse(row.UsageCount, currentUser.IsSuperAdmin);
    }

    public async Task<IReadOnlyList<TimeZoneExportRow>> GetTimeZoneExportRowsAsync(
        TimeZoneSearchFilter filter, CancellationToken cancellationToken)
    {
        var page = await SearchTimeZonesAsync(filter, cancellationToken);

        var ids = page.Items.Select(item => item.Id).ToList();

        var zones = await context.TimeZones
            .AsNoTracking()
            .Where(zone => ids.Contains(zone.Id))
            .ToListAsync(cancellationToken);

        return [.. ids
            .Select(id => zones.FirstOrDefault(zone => zone.Id == id))
            .Where(zone => zone is not null)
            .Select(zone => zone!.ToExportRow())];
    }

    // =====================================================================================
    // Pickers
    // =====================================================================================

    public async Task<GlobalMasterReferenceDataResponse> GetReferenceDataAsync(
        Guid? countryId, CancellationToken cancellationToken)
    {
        // FETCHED ONE AFTER ANOTHER, NOT IN PARALLEL. These four reads share one DbContext,
        // and EF Core permits exactly one operation on a context at a time - starting them
        // together throws "a second operation was started on this context instance". Nor was
        // there anything to win: one context means one connection, so the database would have
        // serialised them anyway.
        var countries = await ActiveOnly(context.Countries.AsNoTracking())
            .OrderBy(country => country.TenantId != null)
            .ThenBy(country => country.SortOrder)
            .ThenBy(country => country.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        var statesQuery = ActiveOnly(context.StateProvinces.AsNoTracking());

        if (countryId.HasValue)
        {
            statesQuery = statesQuery.Where(state => state.CountryId == countryId.Value);
        }

        var states = await statesQuery
            .OrderBy(state => state.TenantId != null)
            .ThenBy(state => state.SortOrder)
            .ThenBy(state => state.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        var currencies = await ActiveOnly(context.Currencies.AsNoTracking())
            .OrderBy(currency => currency.TenantId != null)
            .ThenBy(currency => currency.SortOrder)
            .ThenBy(currency => currency.Code)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        var zones = await ActiveOnly(context.TimeZones.AsNoTracking())
            .OrderBy(zone => zone.TenantId != null)
            .ThenBy(zone => zone.StandardUtcOffsetMinutes)
            .ThenBy(zone => zone.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        return new GlobalMasterReferenceDataResponse(
            [.. countries.Select(country => country.ToLookupResponse())],
            [.. states.Select(state => state.ToLookupResponse())],
            [.. currencies.Select(currency => currency.ToLookupResponse())],
            [.. zones.Select(zone => zone.ToLookupResponse())],
            Describe<GeographicRegion>(),
            Describe<JurisdictionType>(),
            Describe<CurrencyType>(),
            Describe<SymbolPosition>(),
            Describe<RoundingMode>(),
            Describe<MasterDataStatus>());
    }

    public async Task<IReadOnlyList<MasterLookupResponse>> LookupStateProvincesAsync(
        Guid countryId, CancellationToken cancellationToken)
    {
        var states = await ActiveOnly(context.StateProvinces.AsNoTracking())
            .Where(state => state.CountryId == countryId)
            .OrderBy(state => state.TenantId != null)
            .ThenBy(state => state.SortOrder)
            .ThenBy(state => state.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        return [.. states.Select(state => state.ToLookupResponse())];
    }

    public async Task<IReadOnlyList<MasterLookupResponse>> LookupCitiesAsync(
        Guid stateProvinceId, CancellationToken cancellationToken)
    {
        var cities = await ActiveOnly(context.Cities.AsNoTracking())
            .Where(city => city.StateProvinceId == stateProvinceId)
            .OrderBy(city => city.TenantId != null)
            .ThenBy(city => city.SortOrder)
            .ThenBy(city => city.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        return [.. cities.Select(city => city.ToLookupResponse())];
    }

    // =====================================================================================
    // The address-form pickers, usable from any page
    // =====================================================================================

    public async Task<IReadOnlyList<CountryLookupResponse>> LookupCountriesAsync(
        CancellationToken cancellationToken)
    {
        // PROJECTED IN THE DATABASE, not materialised and then shaped. The primary zone and the
        // zone count are a correlated subquery each; pulling every Country aggregate with its
        // CountryTimeZones collection to compute the same two values in memory is the difference
        // between one query and one per country.
        var countries = await ActiveOnly(context.Countries.AsNoTracking())
            .OrderBy(country => country.TenantId != null)
            .ThenBy(country => country.SortOrder)
            .ThenBy(country => country.Name)
            .Take(LookupLimit)
            .Select(country => new
            {
                country.Id,
                country.Code,
                country.Name,
                country.Iso2,
                country.PhoneCountryCode,
                country.HasStates,
                country.DefaultCurrencyId,
                country.DefaultCurrencyCode,
                country.Status,
                country.TenantId,
                country.SortOrder,

                // The primary link if one is marked, otherwise the lowest-sorted link, otherwise
                // nothing. A country mid-edit with no primary still pre-selects something sane.
                PrimaryTimeZoneId = country.CountryTimeZones
                    .Where(link => link.TimeZone.Status == MasterDataStatus.Active)
                    .OrderByDescending(link => link.IsPrimary)
                    .ThenBy(link => link.SortOrder)
                    .Select(link => (Guid?)link.TimeZoneId)
                    .FirstOrDefault(),

                TimeZoneCount = country.CountryTimeZones
                    .Count(link => link.TimeZone.Status == MasterDataStatus.Active)
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. countries.Select(country => new CountryLookupResponse(
                country.Id,
                country.Code,
                country.Name,
                country.Iso2,
                CountryMappingConfig.FlagFor(country.Iso2),
                country.PhoneCountryCode,
                country.HasStates,
                country.DefaultCurrencyId,
                country.DefaultCurrencyCode,
                country.PrimaryTimeZoneId,
                country.TimeZoneCount,
                country.Status,
                country.TenantId is null,
                country.SortOrder))
        ];
    }

    public async Task<IReadOnlyList<MasterLookupResponse>> LookupCitiesAsync(
        Guid? countryId, Guid? stateProvinceId, CancellationToken cancellationToken)
    {
        var query = ActiveOnly(context.Cities.AsNoTracking());

        // THE STATE WINS WHEN BOTH ARE GIVEN. A city already belongs to exactly one state and
        // that state to exactly one country, so filtering on both is redundant at best - and at
        // worst, when a caller sends a stale country alongside a fresh state, it matches nothing
        // and the dropdown looks broken.
        if (stateProvinceId.HasValue)
        {
            query = query.Where(city => city.StateProvinceId == stateProvinceId.Value);
        }
        else if (countryId.HasValue)
        {
            query = query.Where(city => city.CountryId == countryId.Value);
        }

        var cities = await query
            .OrderBy(city => city.TenantId != null)
            .ThenBy(city => city.SortOrder)
            .ThenBy(city => city.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        return [.. cities.Select(city => city.ToLookupResponse())];
    }

    public async Task<IReadOnlyList<CurrencyLookupResponse>> LookupCurrenciesAsync(
        Guid? countryId, CancellationToken cancellationToken)
    {
        // THE COUNTRY MARKS A DEFAULT, IT DOES NOT NARROW THE LIST. An Indian organisation
        // taking a donation in USD is ordinary, so hiding every currency but INR would be wrong;
        // putting INR first and flagging it is what the form actually wants.
        Guid? defaultCurrencyId = null;

        if (countryId.HasValue)
        {
            defaultCurrencyId = await context.Countries
                .AsNoTracking()
                .Where(country => country.Id == countryId.Value)
                .Select(country => country.DefaultCurrencyId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var currencies = await ActiveOnly(context.Currencies.AsNoTracking())
            .OrderBy(currency => currency.TenantId != null)
            .ThenBy(currency => currency.SortOrder)
            .ThenBy(currency => currency.Code)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        return
        [
            .. currencies
                .Select(currency => new CurrencyLookupResponse(
                    currency.Id,
                    currency.Code,
                    currency.Name,
                    currency.Symbol,
                    currency.DecimalPlaces,
                    defaultCurrencyId.HasValue && currency.Id == defaultCurrencyId.Value,
                    currency.Status,
                    currency.IsPlatformRow,
                    currency.SortOrder))
                .OrderByDescending(currency => currency.IsDefaultForCountry)
                .ThenBy(currency => currency.SortOrder)
                .ThenBy(currency => currency.Code)
        ];
    }

    public async Task<(IReadOnlyList<TimeZoneLookupResponse> Zones, bool IsCountryFiltered)>
        LookupTimeZonesAsync(Guid? countryId, CancellationToken cancellationToken)
    {
        if (countryId.HasValue)
        {
            var mapped = await context.CountryTimeZones
                .AsNoTracking()
                .Where(link => link.CountryId == countryId.Value
                    && link.TimeZone.Status == MasterDataStatus.Active)
                .OrderByDescending(link => link.IsPrimary)
                .ThenBy(link => link.SortOrder)
                .ThenBy(link => link.TimeZone.StandardUtcOffsetMinutes)
                .Take(LookupLimit)
                .Select(link => new { link.TimeZone, link.IsPrimary })
                .ToListAsync(cancellationToken);

            // THE FALLBACK, AND THE REASON THIS METHOD RETURNS A FLAG. An unknown country id, a
            // Tenant's own country nobody has mapped zones to yet, or a seed that has not caught
            // up - all three land here. Returning an empty list would leave a required field
            // impossible to satisfy and look, to the person using it, exactly like a bug. So the
            // full catalogue is returned instead and the caller is told it was not narrowed.
            if (mapped.Count > 0)
            {
                return (
                    [.. mapped.Select(entry => entry.TimeZone.ToGeoLookupResponse(entry.IsPrimary))],
                    true);
            }
        }

        var all = await ActiveOnly(context.TimeZones.AsNoTracking())
            .OrderBy(zone => zone.TenantId != null)
            .ThenBy(zone => zone.StandardUtcOffsetMinutes)
            .ThenBy(zone => zone.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        // isPrimaryForCountry is false throughout: with no country in play - or none that
        // matched - "primary" has nothing to be primary for, and claiming otherwise would have
        // the form silently pre-select a zone on the wrong continent.
        return ([.. all.Select(zone => zone.ToGeoLookupResponse(isPrimaryForCountry: false))], false);
    }

    public async Task<(IReadOnlyList<LanguageLookupResponse> Languages, bool IsCountryFiltered)>
        LookupLanguagesAsync(Guid? countryId, CancellationToken cancellationToken)
    {
        if (countryId.HasValue)
        {
            var mapped = await context.CountryLanguages
                .AsNoTracking()
                .Where(link => link.CountryId == countryId.Value
                    && link.Language.Status == MasterDataStatus.Active)
                .OrderByDescending(link => link.IsPrimary)
                .ThenByDescending(link => link.IsOfficial)
                .ThenBy(link => link.SortOrder)
                .ThenBy(link => link.Language.Name)
                .Take(LookupLimit)
                .Select(link => new { link.Language, link.IsPrimary, link.IsOfficial })
                .ToListAsync(cancellationToken);

            // THE FALLBACK, AND THE REASON THIS METHOD RETURNS A FLAG — the same one
            // LookupTimeZonesAsync sets out. An unknown country id, a Tenant's own country
            // nobody has mapped languages to, or a seed that has not caught up all land here.
            // The full catalogue is returned rather than an empty list, because a required
            // field with nothing in it is indistinguishable from a broken page.
            if (mapped.Count > 0)
            {
                return (
                    [.. mapped.Select(entry =>
                        entry.Language.ToGeoLookupResponse(entry.IsPrimary, entry.IsOfficial))],
                    true);
            }
        }

        var all = await ActiveOnly(context.Languages.AsNoTracking())
            .OrderByDescending(language => language.IsDefaultRecommended)
            .ThenBy(language => language.SortOrder)
            .ThenBy(language => language.Name)
            .Take(LookupLimit)
            .ToListAsync(cancellationToken);

        // isPrimaryForCountry and isOfficialInCountry are false throughout: with no country in
        // play — or none that matched — neither has anything to be true OF, and claiming
        // otherwise would have a form silently pre-select a language nobody there speaks.
        return (
            [.. all.Select(language =>
                language.ToGeoLookupResponse(isPrimaryForCountry: false, isOfficialInCountry: false))],
            false);
    }

    public async Task<GeoLookupResponse> GetGeoLookupAsync(
        Guid? countryId, Guid? stateProvinceId, CancellationToken cancellationToken)
    {
        // SEQUENTIAL, NOT PARALLEL, for the reason set out on GetReferenceDataAsync: these share
        // one DbContext, and EF Core permits one operation on it at a time.
        var countries = await LookupCountriesAsync(cancellationToken);

        var states = countryId.HasValue
            ? await LookupStateProvincesAsync(countryId.Value, cancellationToken)
            : [];

        // Cities only once a state or a country narrows them. Unnarrowed, the answer is every
        // city in the catalogue, which is a payload no address form has a use for.
        var cities = countryId.HasValue || stateProvinceId.HasValue
            ? await LookupCitiesAsync(countryId, stateProvinceId, cancellationToken)
            : [];

        var currencies = await LookupCurrenciesAsync(countryId, cancellationToken);
        var (zones, isFiltered) = await LookupTimeZonesAsync(countryId, cancellationToken);
        var (languages, languagesFiltered) = await LookupLanguagesAsync(countryId, cancellationToken);

        return new GeoLookupResponse(
            countries, states, cities, currencies, zones, isFiltered, languages, languagesFiltered);
    }

    // =====================================================================================
    // Shared query helpers
    // =====================================================================================

    /// <summary>
    /// Narrows the already-scoped view to one side of the shared catalogue.
    ///
    /// This is a DISPLAY filter and not a security one, which is worth being clear about: the
    /// global query filter has already removed every row belonging to another Organisation
    /// before this runs. All this does is let the grid show "just the platform rows" or "just
    /// mine", which is how an administrator sees at a glance which rows are theirs to edit.
    ///
    /// Compared on <c>TenantKey</c> rather than on <c>TenantId</c>, because
    /// <c>TenantId == null</c> against a nullable column produces SQL <c>tenant_id = NULL</c>
    /// and matches nothing at all.
    /// </summary>
    private static IQueryable<TEntity> ApplyScope<TEntity>(
        IQueryable<TEntity> query, GlobalMasterSearchFilter filter)
        where TEntity : GlobalMasterEntity
    {
        if (filter.Status.HasValue)
        {
            query = query.Where(entity => entity.Status == filter.Status.Value);
        }

        return filter.Scope switch
        {
            MasterRowScope.Platform => query.Where(entity => entity.TenantKey == Guid.Empty),
            MasterRowScope.Tenant => query.Where(entity => entity.TenantKey != Guid.Empty),
            _ => query
        };
    }

    /// <summary>Active rows only. What every picker wants and no grid does.</summary>
    private static IQueryable<TEntity> ActiveOnly<TEntity>(IQueryable<TEntity> query)
        where TEntity : GlobalMasterEntity =>
        query.Where(entity => entity.Status == MasterDataStatus.Active);

    /// <summary>
    /// The shared sort, understood by all five grids because they all sort on the same four
    /// columns of the base type.
    ///
    /// The DEFAULT is SortOrder then Name, which is what the pickers use, so a grid and its
    /// picker present the same catalogue in the same order. An unrecognised sort expression
    /// falls back to that default rather than throwing - a bad query string should not turn a
    /// list into a 500.
    /// </summary>
    private static IQueryable<TEntity> SortMasters<TEntity>(IQueryable<TEntity> query, string? sort)
        where TEntity : GlobalMasterEntity
    {
        var descending = sort?.EndsWith(" desc", StringComparison.OrdinalIgnoreCase) == true;

        var field = sort?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?.ToLowerInvariant();

        Expression<Func<TEntity, object>> key = field switch
        {
            "name" => entity => entity.Name,
            "code" => entity => entity.Code,
            "status" => entity => entity.Status,
            "updatedatutc" => entity => entity.UpdatedAtUtc!,
            "createdatutc" => entity => entity.CreatedAtUtc,
            _ => entity => entity.SortOrder
        };

        var ordered = descending ? query.OrderByDescending(key) : query.OrderBy(key);

        // A stable tie-break, so paging is deterministic. Without one, two rows sharing a sort
        // order can swap between page 1 and page 2 and a row is silently skipped.
        return field is "name"
            ? ordered.ThenBy(entity => entity.Id)
            : ordered.ThenBy(entity => entity.Name).ThenBy(entity => entity.Id);
    }

    /// <summary>Turns an enum into the value/label pairs the dropdowns bind to.</summary>
    private static IReadOnlyList<EnumOption> Describe<TEnum>() where TEnum : struct, Enum =>
    [
        .. Enum.GetValues<TEnum>()
            .Select(value => new EnumOption(
                value.ToString(),
                Humanise(value.ToString()),
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)))
    ];

    /// <summary>"NorthAmerica" becomes "North america", matching the IAM reference-data labels.</summary>
    private static string Humanise(string value)
    {
        var spaced = string.Concat(
            value.Select((character, index) =>
                index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }
}
