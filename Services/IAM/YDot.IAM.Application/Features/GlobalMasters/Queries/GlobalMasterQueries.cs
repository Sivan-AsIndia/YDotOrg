using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.GlobalMasters.Queries;

// =====================================================================================
// Queries
// =====================================================================================

/// <summary>The country grid.</summary>
public sealed record SearchCountriesQuery(CountrySearchFilter Filter);

/// <summary>One country in full.</summary>
public sealed record GetCountryQuery(Guid CountryId);

/// <summary>CSV export of the country catalogue.</summary>
public sealed record ExportCountriesQuery(CountrySearchFilter Filter);

/// <summary>The state grid.</summary>
public sealed record SearchStateProvincesQuery(StateProvinceSearchFilter Filter);

/// <summary>One state in full.</summary>
public sealed record GetStateProvinceQuery(Guid StateProvinceId);

/// <summary>CSV export of the state catalogue.</summary>
public sealed record ExportStateProvincesQuery(StateProvinceSearchFilter Filter);

/// <summary>The city grid.</summary>
public sealed record SearchCitiesQuery(CitySearchFilter Filter);

/// <summary>One city in full.</summary>
public sealed record GetCityQuery(Guid CityId);

/// <summary>CSV export of the city catalogue.</summary>
public sealed record ExportCitiesQuery(CitySearchFilter Filter);

/// <summary>The currency grid.</summary>
public sealed record SearchCurrenciesQuery(CurrencySearchFilter Filter);

/// <summary>One currency in full.</summary>
public sealed record GetCurrencyQuery(Guid CurrencyId);

/// <summary>CSV export of the currency catalogue.</summary>
public sealed record ExportCurrenciesQuery(CurrencySearchFilter Filter);

/// <summary>The time-zone grid.</summary>
public sealed record SearchTimeZonesQuery(TimeZoneSearchFilter Filter);

/// <summary>One time zone in full.</summary>
public sealed record GetTimeZoneQuery(Guid TimeZoneId);

/// <summary>CSV export of the time-zone catalogue.</summary>
public sealed record ExportTimeZonesQuery(TimeZoneSearchFilter Filter);

/// <summary>Every dropdown the Masters screens need, in one payload.</summary>
public sealed record GetGlobalMasterReferenceDataQuery(Guid? CountryId = null);

/// <summary>Active states beneath one country, for the cascading City form.</summary>
public sealed record LookupStateProvincesQuery(Guid CountryId);

/// <summary>Active cities beneath one state.</summary>
public sealed record LookupCitiesQuery(Guid StateProvinceId);

/// <summary>Active countries, each carrying its default currency and primary time zone.</summary>
public sealed record LookupCountriesQuery;

/// <summary>Active cities, narrowed by state when given and by country when only that is.</summary>
public sealed record LookupGeoCitiesQuery(Guid? CountryId, Guid? StateProvinceId);

/// <summary>Active currencies, with one country's default flagged and sorted first.</summary>
public sealed record LookupCurrenciesQuery(Guid? CountryId);

/// <summary>Active time zones, narrowed to a country's own when it has any mapped.</summary>
public sealed record LookupTimeZonesQuery(Guid? CountryId);

/// <summary>Every address-form picker in one payload.</summary>
/// <summary>The language picker. A null country returns the full catalogue, never an error.</summary>
public sealed record LookupLanguagesQuery(Guid? CountryId);

public sealed record GetGeoLookupQuery(Guid? CountryId, Guid? StateProvinceId);

/// <summary>
/// A time-zone picker's rows plus whether they were actually narrowed to the country asked for.
///
/// THE FLAG TRAVELS WITH THE LIST rather than being inferred by the caller, because the two
/// cases it separates look identical from outside: twelve zones because the country observes
/// twelve, and twelve zones because the country had none mapped and the catalogue was returned
/// whole. A form that cannot tell them apart will either mislabel the field or pre-select a
/// zone on the wrong continent.
/// </summary>
public sealed record TimeZoneLookupListResponse(
    IReadOnlyList<TimeZoneLookupResponse> TimeZones,
    bool IsCountryFiltered);

// =====================================================================================
// Handler
// =====================================================================================

/// <summary>
/// The read side of all five Masters slices.
///
/// ONE HANDLER RATHER THAN FIVE, for the same reason <c>RoleQueryHandler</c> covers both roles
/// and the permission catalogue: every method here is a thin pass-through to the read service,
/// and the only real logic - the paged export loop and the audit row that goes with it - is
/// identical for all five. Five copies of that loop is four places for the page cap to be
/// forgotten.
///
/// THE EXPORTS ARE AUDITED and the reads are not. A grid read is ordinary work; a CSV of the
/// whole catalogue is a copy of the data leaving the system, and it is the event an
/// investigation months later actually looks for.
/// </summary>
public sealed class GlobalMasterQueryHandler(
    IGlobalMasterReadService readService,
    IExportService exports,
    ITokenHasher tokenHasher,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ILogger<GlobalMasterQueryHandler> logger)
{
    /// <summary>
    /// The most pages an export will walk.
    ///
    /// At 100 rows a page that is 50,000 rows, comfortably more than any realistic master
    /// catalogue and a hard stop against an export that would otherwise run until it timed
    /// out.
    /// </summary>
    private const int MaximumExportPages = 500;

    private const int ExportPageSize = 100;

    // ---- Countries -------------------------------------------------------------------

    public async Task<Result<PagedResponse<CountryListItemResponse>>> HandleAsync(
        SearchCountriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching countries. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page,
            query.Filter.PageSize);

        var result = await readService.SearchCountriesAsync(query.Filter, cancellationToken);

        logger.LogInformation(
            "Country search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<CountryDetailResponse>> HandleAsync(
        GetCountryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving country. CountryId: {CountryId}.",
            query.CountryId);

        var country = await readService.GetCountryDetailAsync(query.CountryId, cancellationToken);

        if (country is null)
        {
            logger.LogWarning(
                "Country was not found. CountryId: {CountryId}.",
                query.CountryId);

            return Result.Failure<CountryDetailResponse>(Error.NotFound("That country was not found."));
        }

        logger.LogInformation(
            "Country retrieved successfully. CountryId: {CountryId}.",
            query.CountryId);

        return Result.Success(country);
    }

    public Task<Result<ExportFile>> HandleAsync(
        ExportCountriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting country catalogue export.");

        return ExportAsync(
            query.Filter,
            readService.GetCountryExportRowsAsync,
            "countries",
            nameof(Country),
            cancellationToken);
    }

    // ---- States and provinces ------------------------------------------------------------

    public async Task<Result<PagedResponse<StateProvinceListItemResponse>>> HandleAsync(
        SearchStateProvincesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching states and provinces. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page,
            query.Filter.PageSize);

        var result = await readService.SearchStateProvincesAsync(query.Filter, cancellationToken);

        logger.LogInformation(
            "State and province search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<StateProvinceDetailResponse>> HandleAsync(
        GetStateProvinceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving state or province. StateProvinceId: {StateProvinceId}.",
            query.StateProvinceId);

        var state = await readService.GetStateProvinceDetailAsync(query.StateProvinceId, cancellationToken);

        if (state is null)
        {
            logger.LogWarning(
                "State or province was not found. StateProvinceId: {StateProvinceId}.",
                query.StateProvinceId);

            return Result.Failure<StateProvinceDetailResponse>(Error.NotFound("That state was not found."));
        }

        logger.LogInformation(
            "State or province retrieved successfully. StateProvinceId: {StateProvinceId}.",
            query.StateProvinceId);

        return Result.Success(state);
    }

    public Task<Result<ExportFile>> HandleAsync(
        ExportStateProvincesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting state and province catalogue export.");

        return ExportAsync(
            query.Filter,
            readService.GetStateProvinceExportRowsAsync,
            "states",
            nameof(StateProvince),
            cancellationToken);
    }

    // ---- Cities ------------------------------------------------------------------------------

    public async Task<Result<PagedResponse<CityListItemResponse>>> HandleAsync(
        SearchCitiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching cities. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page,
            query.Filter.PageSize);

        var result = await readService.SearchCitiesAsync(query.Filter, cancellationToken);

        logger.LogInformation(
            "City search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<CityDetailResponse>> HandleAsync(
        GetCityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving city. CityId: {CityId}.",
            query.CityId);

        var city = await readService.GetCityDetailAsync(query.CityId, cancellationToken);

        if (city is null)
        {
            logger.LogWarning(
                "City was not found. CityId: {CityId}.",
                query.CityId);

            return Result.Failure<CityDetailResponse>(Error.NotFound("That city was not found."));
        }

        logger.LogInformation(
            "City retrieved successfully. CityId: {CityId}.",
            query.CityId);

        return Result.Success(city);
    }

    public Task<Result<ExportFile>> HandleAsync(
        ExportCitiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting city catalogue export.");

        return ExportAsync(
            query.Filter,
            readService.GetCityExportRowsAsync,
            "cities",
            nameof(City),
            cancellationToken);
    }

    // ---- Currencies ------------------------------------------------------------------------------

    public async Task<Result<PagedResponse<CurrencyListItemResponse>>> HandleAsync(
        SearchCurrenciesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching currencies. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page,
            query.Filter.PageSize);

        var result = await readService.SearchCurrenciesAsync(query.Filter, cancellationToken);

        logger.LogInformation(
            "Currency search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<CurrencyDetailResponse>> HandleAsync(
        GetCurrencyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving currency. CurrencyId: {CurrencyId}.",
            query.CurrencyId);

        var currency = await readService.GetCurrencyDetailAsync(query.CurrencyId, cancellationToken);

        if (currency is null)
        {
            logger.LogWarning(
                "Currency was not found. CurrencyId: {CurrencyId}.",
                query.CurrencyId);

            return Result.Failure<CurrencyDetailResponse>(Error.NotFound("That currency was not found."));
        }

        logger.LogInformation(
            "Currency retrieved successfully. CurrencyId: {CurrencyId}.",
            query.CurrencyId);

        return Result.Success(currency);
    }

    public Task<Result<ExportFile>> HandleAsync(
        ExportCurrenciesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting currency catalogue export.");

        return ExportAsync(
            query.Filter,
            readService.GetCurrencyExportRowsAsync,
            "currencies",
            nameof(Currency),
            cancellationToken);
    }

    // ---- Time zones ------------------------------------------------------------------------------

    public async Task<Result<PagedResponse<TimeZoneListItemResponse>>> HandleAsync(
        SearchTimeZonesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Searching time zones. Page: {Page}, PageSize: {PageSize}.",
            query.Filter.Page,
            query.Filter.PageSize);

        var result = await readService.SearchTimeZonesAsync(query.Filter, cancellationToken);

        logger.LogInformation(
            "Time zone search completed. TotalCount: {TotalCount}.",
            result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<TimeZoneDetailResponse>> HandleAsync(
        GetTimeZoneQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving time zone. TimeZoneId: {TimeZoneId}.",
            query.TimeZoneId);

        var timeZone = await readService.GetTimeZoneDetailAsync(query.TimeZoneId, cancellationToken);

        if (timeZone is null)
        {
            logger.LogWarning(
                "Time zone was not found. TimeZoneId: {TimeZoneId}.",
                query.TimeZoneId);

            return Result.Failure<TimeZoneDetailResponse>(Error.NotFound("That time zone was not found."));
        }

        logger.LogInformation(
            "Time zone retrieved successfully. TimeZoneId: {TimeZoneId}.",
            query.TimeZoneId);

        return Result.Success(timeZone);
    }

    public Task<Result<ExportFile>> HandleAsync(
        ExportTimeZonesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting time-zone catalogue export.");

        return ExportAsync(
            query.Filter,
            readService.GetTimeZoneExportRowsAsync,
            "time-zones",
            nameof(TimeZoneDefinition),
            cancellationToken);
    }

    // ---- Pickers -----------------------------------------------------------------------------------

    public async Task<Result<GlobalMasterReferenceDataResponse>> HandleAsync(
        GetGlobalMasterReferenceDataQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving global master reference data. CountryId: {CountryId}.",
            query.CountryId);

        var result = await readService.GetReferenceDataAsync(query.CountryId, cancellationToken);

        logger.LogInformation("Global master reference data retrieved successfully.");

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<MasterLookupResponse>>> HandleAsync(
        LookupStateProvincesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up states and provinces. CountryId: {CountryId}.",
            query.CountryId);

        var result = await readService.LookupStateProvincesAsync(query.CountryId, cancellationToken);

        logger.LogInformation(
            "State and province lookup completed. Count: {Count}.",
            result.Count);

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<MasterLookupResponse>>> HandleAsync(
        LookupCitiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up cities. StateProvinceId: {StateProvinceId}.",
            query.StateProvinceId);

        var result = await readService.LookupCitiesAsync(query.StateProvinceId, cancellationToken);

        logger.LogInformation(
            "City lookup completed. Count: {Count}.",
            result.Count);

        return Result.Success(result);
    }

    // ---- The address-form pickers ---------------------------------------------------------

    public async Task<Result<IReadOnlyList<CountryLookupResponse>>> HandleAsync(
        LookupCountriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Looking up active countries.");

        var result = await readService.LookupCountriesAsync(cancellationToken);

        logger.LogInformation(
            "Active country lookup completed. Count: {Count}.",
            result.Count);

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<MasterLookupResponse>>> HandleAsync(
        LookupGeoCitiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up geographic cities. CountryId: {CountryId}, StateProvinceId: {StateProvinceId}.",
            query.CountryId,
            query.StateProvinceId);

        var result = await readService.LookupCitiesAsync(query.CountryId, query.StateProvinceId, cancellationToken);

        logger.LogInformation(
            "Geographic city lookup completed. Count: {Count}.",
            result.Count);

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<CurrencyLookupResponse>>> HandleAsync(
        LookupCurrenciesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up currencies. CountryId: {CountryId}.",
            query.CountryId);

        var result = await readService.LookupCurrenciesAsync(query.CountryId, cancellationToken);

        logger.LogInformation(
            "Currency lookup completed. Count: {Count}.",
            result.Count);

        return Result.Success(result);
    }

    /// <summary>
    /// The time-zone picker.
    ///
    /// NOTE WHAT IS NOT HERE: a not-found branch. Every other Get in this handler turns a missing
    /// row into <c>Result.Failure</c>, and this one deliberately does not, because an unknown or
    /// absent country is not an error for a PICKER - it just means the list cannot be narrowed.
    /// The read service answers with the full catalogue and a flag saying so, and a page that
    /// needs a time zone but never asks for a country gets a working dropdown rather than a 404.
    /// </summary>
    public async Task<Result<TimeZoneLookupListResponse>> HandleAsync(
        LookupTimeZonesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up time zones. CountryId: {CountryId}.",
            query.CountryId);

        var (zones, isCountryFiltered) =
            await readService.LookupTimeZonesAsync(query.CountryId, cancellationToken);

        logger.LogInformation(
            "Time zone lookup completed. Count: {Count}, IsCountryFiltered: {IsCountryFiltered}.",
            zones.Count,
            isCountryFiltered);

        return Result.Success(new TimeZoneLookupListResponse(zones, isCountryFiltered));
    }

    /// <summary>
    /// The language picker.
    ///
    /// NO NOT-FOUND BRANCH HERE EITHER, for the reason given on the time-zone handler above: an
    /// unknown or absent country is not an error for a picker, it just means the list cannot be
    /// narrowed. The setup wizard and user creation both collect a language and never a country,
    /// and both must get a working dropdown rather than a 404.
    /// </summary>
    public async Task<Result<LanguageLookupListResponse>> HandleAsync(
        LookupLanguagesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Looking up languages. CountryId: {CountryId}.",
            query.CountryId);

        var (languages, isCountryFiltered) =
            await readService.LookupLanguagesAsync(query.CountryId, cancellationToken);

        logger.LogInformation(
            "Language lookup completed. Count: {Count}, IsCountryFiltered: {IsCountryFiltered}.",
            languages.Count,
            isCountryFiltered);

        return Result.Success(new LanguageLookupListResponse(languages, isCountryFiltered));
    }

    public async Task<Result<GeoLookupResponse>> HandleAsync(
        GetGeoLookupQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation(
            "Retrieving geographic lookup. CountryId: {CountryId}, StateProvinceId: {StateProvinceId}.",
            query.CountryId,
            query.StateProvinceId);

        var result = await readService.GetGeoLookupAsync(query.CountryId, query.StateProvinceId, cancellationToken);

        logger.LogInformation("Geographic lookup retrieved successfully.");

        return Result.Success(result);
    }

    // ---- The shared export ---------------------------------------------------------------------------------

    /// <summary>
    /// Walks a filtered catalogue page by page, writes it to CSV and records that it happened.
    ///
    /// PAGED RATHER THAN ONE UNBOUNDED READ. A city catalogue can run to tens of thousands of
    /// rows, and asking for all of them in a single query is how an export takes the database
    /// with it. <see cref="MaximumExportPages"/> is the hard stop.
    ///
    /// The filter is MUTATED as the loop walks it, which is safe because it is a per-request
    /// binding model that nothing else holds a reference to - but it does mean the caller must
    /// not reuse the instance afterwards, which no caller does.
    /// </summary>
    private async Task<Result<ExportFile>> ExportAsync<TFilter, TRow>(
        TFilter filter,
        Func<TFilter, CancellationToken, Task<IReadOnlyList<TRow>>> fetchPage,
        string fileName,
        string targetType,
        CancellationToken cancellationToken)
        where TFilter : GlobalMasterSearchFilter
    {
        filter.PageSize = ExportPageSize;
        filter.Page = 1;

        logger.LogInformation(
            "Starting master catalogue export. TargetType: {TargetType}, FileName: {FileName}.",
            targetType,
            fileName);

        var rows = new List<TRow>();

        while (filter.Page <= MaximumExportPages)
        {
            var page = await fetchPage(filter, cancellationToken);

            logger.LogInformation(
                "Master catalogue export page retrieved. TargetType: {TargetType}, Page: {Page}, RowCount: {RowCount}.",
                targetType,
                filter.Page,
                page.Count);

            if (page.Count == 0)
            {
                break;
            }

            rows.AddRange(page);

            // A short page is the last page, so stop rather than issuing one more query that
            // is guaranteed to come back empty.
            if (page.Count < ExportPageSize)
            {
                break;
            }

            filter.Page++;
        }

        var reference = tokenHasher.GenerateReference("EXP");
        var file = exports.ToCsv(rows, fileName, reference);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterExported,
            targetType,
            null,
            null,
            new { RowCount = rows.Count, Reference = reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Master catalogue export completed successfully. TargetType: {TargetType}, RowCount: {RowCount}.",
            targetType,
            rows.Count);

        return Result.Success(file);
    }
}
