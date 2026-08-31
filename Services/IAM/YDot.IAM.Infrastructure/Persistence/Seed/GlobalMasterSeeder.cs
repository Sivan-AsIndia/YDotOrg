using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the PLATFORM half of the global master catalogue: the countries, states, cities,
/// currencies and time zones every Organisation shares.
///
/// EVERY ROW HERE HAS <c>TenantId = null</c>, which is what makes it platform data. Under the
/// scoped query filter that means every Organisation can read it, and
/// <c>GlobalMasterWriteGuard</c> means none of them can change it - only SuperAdmin operating
/// in platform mode. An Organisation that needs a country or city of its own adds one through
/// the API, and that row is stamped with its TenantId and stays invisible to everybody else.
///
/// THE IDS ARE FIXED, NOT GENERATED. They carry over unchanged from the standalone
/// GlobalMaster service, which matters for two reasons: an existing GlobalMaster database can
/// be migrated row for row without remapping anything, and re-running the seeder recognises
/// what it already inserted instead of creating a second India.
///
/// IT IS IDEMPOTENT in exactly the way <see cref="IamDbSeeder"/> is. Each step asks whether a
/// row exists before inserting it, so a master added in a later release reaches an existing
/// database by deploying and restarting - no hand-written migration, and no manual step for
/// somebody to forget.
///
/// FILTERS ARE BYPASSED THROUGHOUT. There is no request here and therefore no ambient
/// Organisation, so the scoped filter would resolve to "platform rows only" - which happens to
/// be right, but only by accident. Reading with IgnoreQueryFilters says what is meant: this
/// code is looking at the whole table.
/// </summary>
public sealed class GlobalMasterSeeder(IamDbContext context, ILogger<GlobalMasterSeeder> logger)
{
    /// <summary>
    /// The BusinessUnit every platform master belongs to.
    ///
    /// Masters are platform-wide, so this is bookkeeping rather than a boundary - but the
    /// column is non-null on <c>ITenantScoped</c> and filling it with the real root keeps a
    /// BusinessUnit-wide report from finding a set of orphaned Guid.Empty rows.
    /// </summary>
    private Guid _businessUnitId;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var businessUnit = await context.BusinessUnits
            .AsNoTracking()
            .OrderBy(unit => unit.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (businessUnit is null)
        {
            // IamDbSeeder creates it, and it runs first. Reaching here means the order changed,
            // which is worth a loud message rather than a set of Guid.Empty rows nobody notices.
            logger.LogWarning(
                "No business unit exists, so the global master catalogue was not seeded. "
                + "IamDbSeeder must run first.");

            return;
        }

        _businessUnitId = businessUnit.Id;

        // ORDER MATTERS. Cities reference states, states reference countries and time zones,
        // and countries reference currencies by code - so each step needs the one above it to
        // have been saved.
        var currencies = await SeedCurrenciesAsync(cancellationToken);
        var timeZones = await SeedTimeZonesAsync(cancellationToken);
        var languages = await SeedLanguagesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var countries = await SeedCountriesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var states = await SeedStateProvincesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var cities = await SeedCitiesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // AFTER the countries, currencies and zones are all committed, because both of these
        // resolve foreign keys against rows the steps above may have only just inserted.
        var currencyLinks = await BackfillCountryCurrenciesAsync(cancellationToken);
        var zoneLinks = await SeedCountryTimeZonesAsync(cancellationToken);
        var languageLinks = await SeedCountryLanguagesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        if (currencies + timeZones + languages + countries + states + cities
            + zoneLinks + currencyLinks + languageLinks > 0)
        {
            logger.LogInformation(
                "Seeded the platform master catalogue: {Currencies} currencies, {TimeZones} time zones, "
                + "{Languages} languages, {Countries} countries, {States} states, {Cities} cities, "
                + "{ZoneLinks} country-to-zone links, {CurrencyLinks} country currency links, "
                + "{LanguageLinks} country-to-language links.",
                currencies, timeZones, languages, countries, states, cities,
                zoneLinks, currencyLinks, languageLinks);
        }
    }

    // =====================================================================================
    // Currencies
    // =====================================================================================

    /// <summary>
    /// The twelve currencies the platform ships with.
    ///
    /// <c>DisplayFormat</c> IS DELIBERATELY LEFT NULL on every one of them. The GlobalMaster
    /// service stored templates like <c>"₹ {amount}"</c>, which is not a .NET numeric format
    /// string and would throw the moment anything passed it to <c>ToString</c>. The symbol and
    /// its position already produce the same rendering, so the correct migration of that field
    /// is to drop it rather than to carry a broken value across.
    /// </summary>
    private async Task<int> SeedCurrenciesAsync(CancellationToken cancellationToken)
    {
        (string Id, string Code, string Name, int Numeric, string Symbol, int Decimals,
            string? MinorUnit, decimal Step, int Order)[] seeds =
        [
            ("55555555-5555-5555-5555-555555555001", "INR", "Indian Rupee", 356, "₹", 2, "Paise", 0.01m, 1),
            ("55555555-5555-5555-5555-555555555002", "USD", "United States Dollar", 840, "$", 2, "Cent", 0.01m, 2),
            ("55555555-5555-5555-5555-555555555003", "SGD", "Singapore Dollar", 702, "S$", 2, "Cent", 0.01m, 3),
            ("55555555-5555-5555-5555-555555555004", "AED", "United Arab Emirates Dirham", 784, "د.إ", 2, "Fils", 0.01m, 4),
            ("55555555-5555-5555-5555-555555555005", "GBP", "Pound Sterling", 826, "£", 2, "Penny", 0.01m, 5),
            ("55555555-5555-5555-5555-555555555006", "EUR", "Euro", 978, "€", 2, "Cent", 0.01m, 6),
            ("55555555-5555-5555-5555-555555555007", "CAD", "Canadian Dollar", 124, "C$", 2, "Cent", 0.01m, 7),
            ("55555555-5555-5555-5555-555555555008", "AUD", "Australian Dollar", 36, "A$", 2, "Cent", 0.01m, 8),

            // Zero decimal places, and the reason the column exists: a yen has no subdivision,
            // so the step is a whole unit and there is no minor-unit name to give.
            ("55555555-5555-5555-5555-555555555009", "JPY", "Japanese Yen", 392, "¥", 0, null, 1m, 9),

            ("55555555-5555-5555-5555-555555555010", "CNY", "Chinese Yuan", 156, "¥", 2, "Fen", 0.01m, 10),
            ("55555555-5555-5555-5555-555555555011", "ZAR", "South African Rand", 710, "R", 2, "Cent", 0.01m, 11),
            ("55555555-5555-5555-5555-555555555012", "CHF", "Swiss Franc", 756, "CHF", 2, "Rappen", 0.01m, 12)
        ];

        var existing = await ExistingIdsAsync(context.Currencies, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Currencies.AddAsync(
                new Currency
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    Code = seed.Code,
                    Name = seed.Name,
                    NumericCode = seed.Numeric,
                    CurrencyType = CurrencyType.Fiat,
                    Symbol = seed.Symbol,
                    SymbolPosition = SymbolPosition.Prefix,
                    DisplayFormat = null,
                    DecimalPlaces = seed.Decimals,
                    MinorUnitName = seed.MinorUnit,
                    RoundingMode = RoundingMode.HalfUp,
                    RoundingStep = seed.Step,
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    // =====================================================================================
    // Time zones
    // =====================================================================================

    private async Task<int> SeedTimeZonesAsync(CancellationToken cancellationToken)
    {
        (string Id, string Iana, string Name, string Short, int Offset, bool Dst, string? Note,
            bool Recommended, int Order)[] seeds =
        [
            ("44444444-4444-4444-4444-444444444001", "Asia/Kolkata", "(UTC+05:30) India Standard Time", "IST", 330, false, null, true, 1),
            ("44444444-4444-4444-4444-444444444002", "Asia/Singapore", "(UTC+08:00) Singapore Standard Time", "SGT", 480, false, null, true, 2),
            ("44444444-4444-4444-4444-444444444003", "Asia/Dubai", "(UTC+04:00) Gulf Standard Time", "GST", 240, false, null, true, 3),
            ("44444444-4444-4444-4444-444444444004", "Asia/Tokyo", "(UTC+09:00) Japan Standard Time", "JST", 540, false, null, false, 4),
            ("44444444-4444-4444-4444-444444444005", "America/New_York", "(UTC-05:00) Eastern Time", "ET", -300, true, "Daylight saving varies by year; follow the IANA rules.", false, 5),
            ("44444444-4444-4444-4444-444444444006", "America/Los_Angeles", "(UTC-08:00) Pacific Time", "PT", -480, true, "Daylight saving varies by year; follow the IANA rules.", false, 6),
            ("44444444-4444-4444-4444-444444444007", "America/Toronto", "(UTC-05:00) Eastern Time - Canada", "ET", -300, true, "Daylight saving varies by year; follow the IANA rules.", false, 7),
            ("44444444-4444-4444-4444-444444444008", "Europe/London", "(UTC+00:00) Greenwich Mean Time", "GMT", 0, true, "The United Kingdom observes British Summer Time.", false, 8),
            ("44444444-4444-4444-4444-444444444009", "Europe/Paris", "(UTC+01:00) Central European Time", "CET", 60, true, "Daylight saving varies by year; follow the IANA rules.", false, 9),
            ("44444444-4444-4444-4444-444444444010", "Europe/Berlin", "(UTC+01:00) Central European Time", "CET", 60, true, "Daylight saving varies by year; follow the IANA rules.", false, 10),
            ("44444444-4444-4444-4444-444444444011", "Australia/Sydney", "(UTC+10:00) Australian Eastern Time", "AET", 600, true, "Daylight saving varies by state and year; follow the IANA rules.", false, 11),
            ("44444444-4444-4444-4444-444444444012", "Africa/Johannesburg", "(UTC+02:00) South Africa Standard Time", "SAST", 120, false, null, false, 12),
            ("44444444-4444-4444-4444-444444444013", "Asia/Shanghai", "(UTC+08:00) China Standard Time", "CST", 480, false, null, true, 13),
            ("44444444-4444-4444-4444-444444444014", "America/Chicago", "(UTC-06:00) Central Time", "CT", -360, true, "Daylight saving varies by year; follow the IANA rules.", false, 14),
            ("44444444-4444-4444-4444-444444444015", "America/Denver", "(UTC-07:00) Mountain Time", "MT", -420, true, "Daylight saving varies by year; follow the IANA rules.", false, 15),
            ("44444444-4444-4444-4444-444444444016", "America/Phoenix", "(UTC-07:00) Mountain Time - Arizona", "MST", -420, false, null, false, 16),
            ("44444444-4444-4444-4444-444444444017", "America/Anchorage", "(UTC-09:00) Alaska Time", "AKT", -540, true, "Daylight saving varies by year; follow the IANA rules.", false, 17),
            ("44444444-4444-4444-4444-444444444018", "Pacific/Honolulu", "(UTC-10:00) Hawaii-Aleutian Time", "HST", -600, false, null, false, 18),
            ("44444444-4444-4444-4444-444444444019", "America/Vancouver", "(UTC-08:00) Pacific Time - Canada", "PT", -480, true, "Daylight saving varies by year; follow the IANA rules.", false, 19),
            ("44444444-4444-4444-4444-444444444020", "America/Edmonton", "(UTC-07:00) Mountain Time - Canada", "MT", -420, true, "Daylight saving varies by year; follow the IANA rules.", false, 20),
            ("44444444-4444-4444-4444-444444444021", "America/Winnipeg", "(UTC-06:00) Central Time - Canada", "CT", -360, true, "Daylight saving varies by year; follow the IANA rules.", false, 21),
            ("44444444-4444-4444-4444-444444444022", "America/Halifax", "(UTC-04:00) Atlantic Time", "AT", -240, true, "Daylight saving varies by year; follow the IANA rules.", false, 22),
            ("44444444-4444-4444-4444-444444444023", "America/St_Johns", "(UTC-03:30) Newfoundland Time", "NT", -210, true, "A half-hour offset plus a further half hour of daylight saving.", false, 23),
            ("44444444-4444-4444-4444-444444444024", "Australia/Perth", "(UTC+08:00) Australian Western Time", "AWT", 480, false, null, false, 24),
            ("44444444-4444-4444-4444-444444444025", "Australia/Brisbane", "(UTC+10:00) Australian Eastern Time - Queensland", "AET", 600, false, null, false, 25),
            ("44444444-4444-4444-4444-444444444026", "Australia/Adelaide", "(UTC+09:30) Australian Central Time", "ACT", 570, true, "Daylight saving varies by year; follow the IANA rules.", false, 26),
            ("44444444-4444-4444-4444-444444444027", "Australia/Darwin", "(UTC+09:30) Australian Central Time - Northern Territory", "ACST", 570, false, null, false, 27),
            ("44444444-4444-4444-4444-444444444028", "Australia/Hobart", "(UTC+10:00) Australian Eastern Time - Tasmania", "AET", 600, true, "Daylight saving varies by year; follow the IANA rules.", false, 28),

            // UTC ITSELF, which was missing and is not a nicety. Several forms - user creation
            // among them - default their time zone to "UTC", and with no such row that default
            // matched no option in the dropdown: the field rendered blank and the person had to
            // pick something before a valid form would submit. It is linked to no country on
            // purpose; it belongs to all of them and to none.
            ("44444444-4444-4444-4444-444444444029", "UTC", "(UTC+00:00) Coordinated Universal Time", "UTC", 0, false, null, false, 0)
        ];

        var existing = await ExistingIdsAsync(context.TimeZones, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.TimeZones.AddAsync(
                new TimeZoneDefinition
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,

                    // Derived rather than written out, so the seed cannot disagree with what the
                    // create endpoint would have produced for the same key.
                    Code = TimeZoneMappingConfig.ToCode(seed.Iana),

                    IanaKey = seed.Iana,
                    Name = seed.Name,
                    ShortName = seed.Short,
                    StandardUtcOffsetMinutes = seed.Offset,
                    SupportsDaylightSaving = seed.Dst,

                    // The note is null wherever the zone does not observe daylight saving. The
                    // GlobalMaster data carried sentences like "India does not observe daylight
                    // saving time", which restate the boolean beside them and would violate the
                    // ck_gm_time_zones_dst_note constraint.
                    DaylightSavingRuleNote = seed.Note,

                    IsDefaultRecommended = seed.Recommended,
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    // =====================================================================================
    // Countries
    // =====================================================================================

    private async Task<int> SeedCountriesAsync(CancellationToken cancellationToken)
    {
        (string Id, string Code, string Name, string? Official, GeographicRegion Region, string Iso2,
            string Iso3, string Numeric, string Currency, bool HasStates, string? Postal, string Phone,
            int Order)[] seeds =
        [
            ("11111111-1111-1111-1111-111111111001", "IN", "India", "Republic of India", GeographicRegion.Asia, "IN", "IND", "356", "INR", true, @"^\d{6}$", "+91", 1),
            ("11111111-1111-1111-1111-111111111002", "US", "United States", "United States of America", GeographicRegion.NorthAmerica, "US", "USA", "840", "USD", true, @"^\d{5}(-\d{4})?$", "+1", 2),
            ("11111111-1111-1111-1111-111111111003", "SG", "Singapore", "Republic of Singapore", GeographicRegion.Asia, "SG", "SGP", "702", "SGD", false, @"^\d{6}$", "+65", 3),
            ("11111111-1111-1111-1111-111111111004", "AU", "Australia", "Commonwealth of Australia", GeographicRegion.Oceania, "AU", "AUS", "036", "AUD", true, @"^\d{4}$", "+61", 4),
            ("11111111-1111-1111-1111-111111111005", "CA", "Canada", null, GeographicRegion.NorthAmerica, "CA", "CAN", "124", "CAD", true, null, "+1", 5),
            ("11111111-1111-1111-1111-111111111006", "GB", "United Kingdom", "United Kingdom of Great Britain and Northern Ireland", GeographicRegion.Europe, "GB", "GBR", "826", "GBP", true, null, "+44", 6),
            ("11111111-1111-1111-1111-111111111007", "DE", "Germany", "Federal Republic of Germany", GeographicRegion.Europe, "DE", "DEU", "276", "EUR", true, @"^\d{5}$", "+49", 7),
            ("11111111-1111-1111-1111-111111111008", "FR", "France", "French Republic", GeographicRegion.Europe, "FR", "FRA", "250", "EUR", true, @"^\d{5}$", "+33", 8),
            ("11111111-1111-1111-1111-111111111009", "JP", "Japan", null, GeographicRegion.Asia, "JP", "JPN", "392", "JPY", true, @"^\d{3}-\d{4}$", "+81", 9),
            ("11111111-1111-1111-1111-111111111010", "CN", "China", "People's Republic of China", GeographicRegion.Asia, "CN", "CHN", "156", "CNY", true, @"^\d{6}$", "+86", 10),
            ("11111111-1111-1111-1111-111111111011", "AE", "United Arab Emirates", null, GeographicRegion.MiddleEast, "AE", "ARE", "784", "AED", true, null, "+971", 11),
            ("11111111-1111-1111-1111-111111111012", "ZA", "South Africa", "Republic of South Africa", GeographicRegion.Africa, "ZA", "ZAF", "710", "ZAR", true, @"^\d{4}$", "+27", 12)
        ];

        var existing = await ExistingIdsAsync(context.Countries, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Countries.AddAsync(
                new Country
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    Code = seed.Code,
                    Name = seed.Name,
                    OfficialName = seed.Official,
                    Region = seed.Region,
                    Iso2 = seed.Iso2,
                    Iso3 = seed.Iso3,
                    NumericCode = seed.Numeric,
                    DefaultCurrencyCode = seed.Currency,
                    HasStates = seed.HasStates,
                    PostalCodePattern = seed.Postal,
                    PhoneCountryCode = seed.Phone,
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    // =====================================================================================
    // States and provinces
    // =====================================================================================

    private async Task<int> SeedStateProvincesAsync(CancellationToken cancellationToken)
    {
        const string India = "11111111-1111-1111-1111-111111111001";
        const string UnitedStates = "11111111-1111-1111-1111-111111111002";
        const string Australia = "11111111-1111-1111-1111-111111111004";
        const string Canada = "11111111-1111-1111-1111-111111111005";
        const string Emirates = "11111111-1111-1111-1111-111111111011";

        const string IndiaTimeZone = "44444444-4444-4444-4444-444444444001";
        const string EasternTime = "44444444-4444-4444-4444-444444444005";
        const string PacificTime = "44444444-4444-4444-4444-444444444006";
        const string TorontoTime = "44444444-4444-4444-4444-444444444007";
        const string SydneyTime = "44444444-4444-4444-4444-444444444011";
        const string DubaiTime = "44444444-4444-4444-4444-444444444003";

        (string Id, string CountryId, string Code, string Name, JurisdictionType Type, string? Other,
            string? Gst, string? TimeZoneId, int Order)[] seeds =
        [
            ("22222222-2222-2222-2222-222222222001", India, "TN", "Tamil Nadu", JurisdictionType.State, null, "33", IndiaTimeZone, 1),
            ("22222222-2222-2222-2222-222222222002", India, "KA", "Karnataka", JurisdictionType.State, null, "29", IndiaTimeZone, 2),
            ("22222222-2222-2222-2222-222222222003", India, "KL", "Kerala", JurisdictionType.State, null, "32", IndiaTimeZone, 3),
            ("22222222-2222-2222-2222-222222222004", India, "MH", "Maharashtra", JurisdictionType.State, null, "27", IndiaTimeZone, 4),
            ("22222222-2222-2222-2222-222222222005", UnitedStates, "CA", "California", JurisdictionType.State, null, null, PacificTime, 5),
            ("22222222-2222-2222-2222-222222222006", UnitedStates, "NY", "New York", JurisdictionType.State, null, null, EasternTime, 6),
            ("22222222-2222-2222-2222-222222222007", UnitedStates, "TX", "Texas", JurisdictionType.State, null, null, null, 7),
            ("22222222-2222-2222-2222-222222222008", Canada, "ON", "Ontario", JurisdictionType.Province, null, null, TorontoTime, 8),
            ("22222222-2222-2222-2222-222222222009", Canada, "BC", "British Columbia", JurisdictionType.Province, null, null, PacificTime, 9),
            ("22222222-2222-2222-2222-222222222010", Australia, "NSW", "New South Wales", JurisdictionType.State, null, null, SydneyTime, 10),
            ("22222222-2222-2222-2222-222222222011", Australia, "VIC", "Victoria", JurisdictionType.State, null, null, SydneyTime, 11),

            // The reason JurisdictionType has an Other member: an emirate is none of the
            // enumerated kinds, and the free-text description is what the UI shows instead.
            ("22222222-2222-2222-2222-222222222012", Emirates, "DU", "Dubai", JurisdictionType.Other, "Emirate", null, DubaiTime, 12)
        ];

        var existing = await ExistingIdsAsync(context.StateProvinces, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.StateProvinces.AddAsync(
                new StateProvince
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    Code = seed.Code,
                    Name = seed.Name,
                    DisplayName = seed.Name,
                    CountryId = Guid.Parse(seed.CountryId),
                    JurisdictionType = seed.Type,
                    OtherJurisdictionType = seed.Other,
                    GstStateCode = seed.Gst,
                    DefaultTimeZoneId = seed.TimeZoneId is null ? null : Guid.Parse(seed.TimeZoneId),
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    // =====================================================================================
    // Cities
    // =====================================================================================

    private async Task<int> SeedCitiesAsync(CancellationToken cancellationToken)
    {
        const string TamilNadu = "22222222-2222-2222-2222-222222222001";
        const string Karnataka = "22222222-2222-2222-2222-222222222002";
        const string Kerala = "22222222-2222-2222-2222-222222222003";
        const string Maharashtra = "22222222-2222-2222-2222-222222222004";
        const string California = "22222222-2222-2222-2222-222222222005";
        const string NewYork = "22222222-2222-2222-2222-222222222006";
        const string Ontario = "22222222-2222-2222-2222-222222222008";
        const string Dubai = "22222222-2222-2222-2222-222222222012";

        (string Id, string StateId, string Code, string Name, bool Metro, decimal Latitude,
            decimal Longitude, string? Postal, int Order)[] seeds =
        [
            ("33333333-3333-3333-3333-333333333001", TamilNadu, "CHN", "Chennai", true, 13.0827m, 80.2707m, @"^\d{6}$", 1),
            ("33333333-3333-3333-3333-333333333002", TamilNadu, "CBE", "Coimbatore", true, 11.0168m, 76.9558m, @"^\d{6}$", 2),
            ("33333333-3333-3333-3333-333333333003", TamilNadu, "MDU", "Madurai", false, 9.9252m, 78.1198m, @"^\d{6}$", 3),
            ("33333333-3333-3333-3333-333333333004", Karnataka, "BLR", "Bengaluru", true, 12.9716m, 77.5946m, @"^\d{6}$", 4),
            ("33333333-3333-3333-3333-333333333005", Karnataka, "MYS", "Mysuru", false, 12.2958m, 76.6394m, @"^\d{6}$", 5),
            ("33333333-3333-3333-3333-333333333006", Kerala, "COK", "Kochi", true, 9.9312m, 76.2673m, @"^\d{6}$", 6),
            ("33333333-3333-3333-3333-333333333007", Kerala, "TVM", "Thiruvananthapuram", true, 8.5241m, 76.9366m, @"^\d{6}$", 7),
            ("33333333-3333-3333-3333-333333333008", Maharashtra, "MUM", "Mumbai", true, 19.0760m, 72.8777m, @"^\d{6}$", 8),
            ("33333333-3333-3333-3333-333333333009", California, "LAX", "Los Angeles", true, 34.0522m, -118.2437m, null, 9),
            ("33333333-3333-3333-3333-333333333010", NewYork, "NYC", "New York City", true, 40.7128m, -74.0060m, null, 10),
            ("33333333-3333-3333-3333-333333333011", Ontario, "TOR", "Toronto", true, 43.6532m, -79.3832m, null, 11),
            ("33333333-3333-3333-3333-333333333012", Dubai, "DXB", "Dubai", true, 25.2048m, 55.2708m, null, 12)
        ];

        // THE COUNTRY IS LOOKED UP FROM THE STATE, exactly as the create handler does it. The
        // seed could name both, and then a typo in one of twelve rows would be a denormalised
        // column silently disagreeing with its source - the one failure the whole design of
        // City.CountryId is meant to make unreachable.
        var stateCountries = await context.StateProvinces
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(state => new { state.Id, state.CountryId })
            .ToDictionaryAsync(state => state.Id, state => state.CountryId, cancellationToken);

        var existing = await ExistingIdsAsync(context.Cities, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse(seed.Id);
            var stateId = Guid.Parse(seed.StateId);

            if (existing.Contains(id))
            {
                continue;
            }

            if (!stateCountries.TryGetValue(stateId, out var countryId))
            {
                logger.LogWarning(
                    "City {CityCode} names state {StateId}, which does not exist. Skipped.",
                    seed.Code, stateId);

                continue;
            }

            await context.Cities.AddAsync(
                new City
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    Code = seed.Code,
                    Name = seed.Name,
                    DisplayName = seed.Name,
                    StateProvinceId = stateId,
                    CountryId = countryId,
                    DefaultPostalCodePattern = seed.Postal,
                    IsMetro = seed.Metro,
                    Latitude = seed.Latitude,
                    Longitude = seed.Longitude,
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    /// <summary>
    /// The ids already present, read once per table.
    ///
    /// One read and a set lookup rather than an EXISTS query per seed row: twelve round trips
    /// per table becomes one, and the seeder runs on every application start.
    /// </summary>
    /// <summary>
    /// Points every country at its default currency by FOREIGN KEY, not just by code.
    ///
    /// A BACKFILL RATHER THAN PART OF <c>SeedCountriesAsync</c>, and run every time rather than
    /// only on a fresh database. The country rows already existed in deployed environments with
    /// nothing but <c>DefaultCurrencyCode</c> set, so a fix that only applied to newly-inserted
    /// rows would have left every existing installation exactly as broken as before.
    ///
    /// It only ever fills a null. A country an administrator has deliberately pointed at a
    /// different currency keeps that choice — this repairs missing links, it does not overrule
    /// anybody.
    /// </summary>
    private async Task<int> BackfillCountryCurrenciesAsync(CancellationToken cancellationToken)
    {
        var currencyIdsByCode = await context.Currencies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(currency => currency.TenantId == null)
            .ToDictionaryAsync(
                currency => currency.Code, currency => currency.Id, StringComparer.OrdinalIgnoreCase,
                cancellationToken);

        var countries = await context.Countries
            .IgnoreQueryFilters()
            .Where(country => country.DefaultCurrencyId == null && country.DefaultCurrencyCode != null)
            .ToListAsync(cancellationToken);

        var linked = 0;

        foreach (var country in countries)
        {
            if (currencyIdsByCode.TryGetValue(country.DefaultCurrencyCode!, out var currencyId))
            {
                country.DefaultCurrencyId = currencyId;
                linked++;
            }
            else
            {
                // A country naming a currency that is not in the catalogue. Left as it stands
                // rather than guessed at, and said out loud, because the fix is to seed the
                // currency - silently inventing a link would hide that.
                logger.LogWarning(
                    "Country {CountryCode} names currency {CurrencyCode}, which is not in the "
                    + "catalogue. Its default currency link was left unset.",
                    country.Code, country.DefaultCurrencyCode);
            }
        }

        return linked;
    }

    /// <summary>
    /// Maps each country to the time zones observed inside it.
    ///
    /// WHY THE MULTI-ZONE COUNTRIES ARE SPELLED OUT IN FULL. The brief asks that a country with
    /// several zones show all of them, and the United States, Canada and Australia are exactly
    /// the cases where a one-zone-per-country shortcut looks right in testing (somebody in New
    /// York tries it) and is wrong in production (somebody in Denver does). Seven rows for the
    /// United States, six each for Canada and Australia, one for everybody else.
    ///
    /// EXACTLY ONE ROW PER COUNTRY IS PRIMARY. That is the zone a form pre-selects the moment a
    /// country is picked, so it must be the one most of the population lives in rather than
    /// whichever row the database returns first.
    /// </summary>
    private async Task<int> SeedCountryTimeZonesAsync(CancellationToken cancellationToken)
    {
        const string CountryPrefix = "11111111-1111-1111-1111-1111111110";
        const string ZonePrefix = "44444444-4444-4444-4444-4444444440";

        // (link id suffix, country suffix, zone suffix, is primary, order within the country)
        (string Link, string Country, string Zone, bool Primary, int Order)[] seeds =
        [
            // India, and the single-zone countries.
            ("001", "01", "01", true, 1),   // IN -> Asia/Kolkata
            ("002", "03", "02", true, 1),   // SG -> Asia/Singapore
            ("003", "06", "08", true, 1),   // GB -> Europe/London
            ("004", "07", "10", true, 1),   // DE -> Europe/Berlin
            ("005", "08", "09", true, 1),   // FR -> Europe/Paris
            ("006", "09", "04", true, 1),   // JP -> Asia/Tokyo
            ("007", "11", "03", true, 1),   // AE -> Asia/Dubai
            ("008", "12", "12", true, 1),   // ZA -> Africa/Johannesburg

            // China observes a single zone nationwide despite its width, which is a genuine
            // property of the country rather than an omission here.
            ("009", "10", "13", true, 1),   // CN -> Asia/Shanghai

            // The United States: six zones across the states plus Arizona, which sits on
            // Mountain time and does not observe daylight saving.
            ("010", "02", "05", true, 1),   // US -> America/New_York
            ("011", "02", "14", false, 2),  // US -> America/Chicago
            ("012", "02", "15", false, 3),  // US -> America/Denver
            ("013", "02", "16", false, 4),  // US -> America/Phoenix
            ("014", "02", "06", false, 5),  // US -> America/Los_Angeles
            ("015", "02", "17", false, 6),  // US -> America/Anchorage
            ("016", "02", "18", false, 7),  // US -> Pacific/Honolulu

            // Canada, west to east, including Newfoundland's half-hour offset.
            ("017", "05", "07", true, 1),   // CA -> America/Toronto
            ("018", "05", "22", false, 2),  // CA -> America/Halifax
            ("019", "05", "21", false, 3),  // CA -> America/Winnipeg
            ("020", "05", "20", false, 4),  // CA -> America/Edmonton
            ("021", "05", "19", false, 5),  // CA -> America/Vancouver
            ("022", "05", "23", false, 6),  // CA -> America/St_Johns

            // Australia. Brisbane and Darwin share an offset with Sydney and Adelaide but not
            // their daylight saving, which is why they are separate zones rather than duplicates.
            ("023", "04", "11", true, 1),   // AU -> Australia/Sydney
            ("024", "04", "25", false, 2),  // AU -> Australia/Brisbane
            ("025", "04", "26", false, 3),  // AU -> Australia/Adelaide
            ("026", "04", "27", false, 4),  // AU -> Australia/Darwin
            ("027", "04", "24", false, 5),  // AU -> Australia/Perth
            ("028", "04", "28", false, 6)   // AU -> Australia/Hobart
        ];

        var existing = await context.CountryTimeZones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(link => link.Id)
            .ToListAsync(cancellationToken);

        var existingIds = new HashSet<Guid>(existing);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse($"66666666-6666-6666-6666-66666666{seed.Link.PadLeft(4, '0')}");

            if (existingIds.Contains(id))
            {
                continue;
            }

            await context.CountryTimeZones.AddAsync(
                new CountryTimeZone
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    CountryId = Guid.Parse(CountryPrefix + seed.Country),
                    TimeZoneId = Guid.Parse(ZonePrefix + seed.Zone),
                    IsPrimary = seed.Primary,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    // =====================================================================================
    // Languages
    // =====================================================================================

    /// <summary>
    /// The languages the platform ships with.
    ///
    /// WHAT THIS REPLACES. Language was the last field on the platform still driven by literals
    /// in the browser: the setup wizard took free text behind a ten-character maxlength, user
    /// creation offered four hard-coded options, and lead capture fell back to
    /// <c>['English', 'Tamil', 'Hindi', 'Malayalam']</c>. Three screens, three vocabularies, and
    /// nothing in the database to reconcile them against.
    ///
    /// THE CULTURE CODES MATCH WHAT IS ALREADY STORED. <c>en-GB</c>, <c>en-IN</c>, <c>en-US</c>
    /// and <c>hi-IN</c> are the four values user creation was already writing, and the wizard
    /// defaults to <c>en-IN</c>. Seeding those exact strings is what lets every existing row
    /// resolve to a catalogue entry instead of opening a picker with nothing selected.
    ///
    /// NativeName CARRIES THE LANGUAGE'S OWN SCRIPT, and a picker shows it beside the English
    /// name rather than instead of it. A person choosing their own language looks for the word
    /// they recognise, which is rarely the English one; an administrator setting up somebody
    /// else's account needs the English one. Both are stored, so neither has to be guessed at.
    /// </summary>
    private async Task<int> SeedLanguagesAsync(CancellationToken cancellationToken)
    {
        // (id suffix, culture, name, native name, iso639-1, iso639-3, recommended, order)
        (string Id, string Culture, string Name, string Native, string Iso1, string Iso3,
            bool Recommended, int Order)[] seeds =
        [
            ("001", "en-IN", "English (India)", "English", "en", "eng", true, 1),
            ("002", "en-GB", "English (United Kingdom)", "English", "en", "eng", true, 2),
            ("003", "en-US", "English (United States)", "English", "en", "eng", false, 3),
            ("004", "hi-IN", "Hindi", "हिन्दी", "hi", "hin", true, 4),
            ("005", "ta-IN", "Tamil", "தமிழ்", "ta", "tam", false, 5),
            ("006", "te-IN", "Telugu", "తెలుగు", "te", "tel", false, 6),
            ("007", "kn-IN", "Kannada", "ಕನ್ನಡ", "kn", "kan", false, 7),
            ("008", "ml-IN", "Malayalam", "മലയാളം", "ml", "mal", false, 8),
            ("009", "mr-IN", "Marathi", "मराठी", "mr", "mar", false, 9),
            ("010", "bn-IN", "Bengali", "বাংলা", "bn", "ben", false, 10),
            ("011", "gu-IN", "Gujarati", "ગુજરાતી", "gu", "guj", false, 11),
            ("012", "pa-IN", "Punjabi", "ਪੰਜਾਬੀ", "pa", "pan", false, 12),
            ("013", "ur-IN", "Urdu", "اردو", "ur", "urd", false, 13),
            ("014", "fr-FR", "French", "Français", "fr", "fra", false, 14),
            ("015", "de-DE", "German", "Deutsch", "de", "deu", false, 15),
            ("016", "ja-JP", "Japanese", "日本語", "ja", "jpn", false, 16),
            ("017", "zh-CN", "Chinese (Simplified)", "简体中文", "zh", "zho", false, 17),
            ("018", "ar-AE", "Arabic", "العربية", "ar", "ara", false, 18),
            ("019", "en-SG", "English (Singapore)", "English", "en", "eng", false, 19),
            ("020", "en-AU", "English (Australia)", "English", "en", "eng", false, 20),
            ("021", "en-CA", "English (Canada)", "English", "en", "eng", false, 21),
            ("022", "en-ZA", "English (South Africa)", "English", "en", "eng", false, 22),
            ("023", "af-ZA", "Afrikaans", "Afrikaans", "af", "afr", false, 23),
            ("024", "zu-ZA", "Zulu", "isiZulu", "zu", "zul", false, 24),
            ("025", "ms-SG", "Malay", "Bahasa Melayu", "ms", "msa", false, 25)
        ];

        var existing = await ExistingIdsAsync(context.Languages, cancellationToken);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse($"77777777-7777-7777-7777-77777777{seed.Id.PadLeft(4, '0')}");

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Languages.AddAsync(
                new Language
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    Code = LanguageMappingConfig.ToCode(seed.Culture),
                    CultureCode = seed.Culture,
                    Name = seed.Name,
                    NativeName = seed.Native,
                    Iso2 = seed.Iso1,
                    Iso3 = seed.Iso3,
                    // Arabic and Urdu are the two right-to-left scripts in this set.
                    IsRightToLeft = seed.Iso1 is "ar" or "ur",
                    IsDefaultRecommended = seed.Recommended,
                    Status = MasterDataStatus.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    /// <summary>
    /// Maps each country to the languages used in it.
    ///
    /// THE MULTI-LANGUAGE COUNTRIES ARE SPELLED OUT FOR THE SAME REASON THE MULTI-ZONE ONES ARE.
    /// India is the case that matters here: a picker offering only Hindi would be wrong for most
    /// of the country, and the platform's own seeded states are Tamil Nadu, Karnataka, Kerala and
    /// Maharashtra - four different languages between them. Canada gets French, Singapore its
    /// four official languages, South Africa three of its eleven.
    ///
    /// EXACTLY ONE ROW PER COUNTRY IS PRIMARY: the language a form pre-selects once the country
    /// is chosen. For India that is English (India) rather than Hindi, because en-IN is what the
    /// platform's existing rows already store and what administration is actually conducted in.
    /// </summary>
    private async Task<int> SeedCountryLanguagesAsync(CancellationToken cancellationToken)
    {
        const string CountryPrefix = "11111111-1111-1111-1111-1111111110";

        // NOT A PREFIX-PLUS-SUFFIX CONSTANT like CountryPrefix above, and that difference is
        // deliberate. The country ids are written out in full by SeedCountriesAsync, so a
        // 34-character prefix plus a two-digit suffix reproduces them exactly. The LANGUAGE ids
        // are built by SeedLanguagesAsync with PadLeft(4, '0'), so the same trick lands one
        // character out - "...777777777001" where the row is "...777777770001" - and every link
        // below fails its foreign key. Building the id the same way the row was built is what
        // stops the two drifting again.

        // (link id suffix, country suffix, language suffix, primary, official, order)
        (string Link, string Country, string Language, bool Primary, bool Official, int Order)[] seeds =
        [
            // India. English first because it is what the stored rows hold; then Hindi, then the
            // regional languages of the states this platform actually seeds.
            ("001", "01", "01", true, true, 1),    // IN -> en-IN
            ("002", "01", "04", false, true, 2),   // IN -> hi-IN
            ("003", "01", "05", false, true, 3),   // IN -> ta-IN
            ("004", "01", "06", false, true, 4),   // IN -> te-IN
            ("005", "01", "07", false, true, 5),   // IN -> kn-IN
            ("006", "01", "08", false, true, 6),   // IN -> ml-IN
            ("007", "01", "09", false, true, 7),   // IN -> mr-IN
            ("008", "01", "10", false, true, 8),   // IN -> bn-IN
            ("009", "01", "11", false, true, 9),   // IN -> gu-IN
            ("010", "01", "12", false, true, 10),  // IN -> pa-IN
            ("011", "01", "13", false, true, 11),  // IN -> ur-IN

            // United States.
            ("012", "02", "03", true, true, 1),    // US -> en-US

            // Singapore: English is the language of administration, and the other three are
            // official alongside it.
            ("013", "03", "19", true, true, 1),    // SG -> en-SG
            ("014", "03", "17", false, true, 2),   // SG -> zh-CN
            ("015", "03", "05", false, true, 3),   // SG -> ta-IN
            ("016", "03", "25", false, true, 4),   // SG -> ms-SG

            ("017", "04", "20", true, true, 1),    // AU -> en-AU

            // Canada is officially bilingual, so both rows are official and one is primary.
            ("018", "05", "21", true, true, 1),    // CA -> en-CA
            ("019", "05", "14", false, true, 2),   // CA -> fr-FR

            ("020", "06", "02", true, true, 1),    // GB -> en-GB
            ("021", "07", "15", true, true, 1),    // DE -> de-DE
            ("022", "08", "14", true, true, 1),    // FR -> fr-FR
            ("023", "09", "16", true, true, 1),    // JP -> ja-JP
            ("024", "10", "17", true, true, 1),    // CN -> zh-CN

            // The Emirates: Arabic is official, English is what business is conducted in.
            ("025", "11", "18", true, true, 1),    // AE -> ar-AE
            ("026", "11", "02", false, false, 2),  // AE -> en-GB

            // South Africa has eleven official languages; these are the three most widely used.
            ("027", "12", "22", true, true, 1),    // ZA -> en-ZA
            ("028", "12", "24", false, true, 2),   // ZA -> zu-ZA
            ("029", "12", "23", false, true, 3)    // ZA -> af-ZA
        ];

        var existing = await context.CountryLanguages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(link => link.Id)
            .ToListAsync(cancellationToken);

        var existingIds = new HashSet<Guid>(existing);

        // THE TARGETS ARE CHECKED BEFORE THE LINK IS WRITTEN, the same way SeedCitiesAsync checks
        // that a city's state exists. A link pointing at a language or country that is not in the
        // catalogue is a FOREIGN KEY VIOLATION, and because the seeder runs inside
        // InitialiseDatabaseAsync a violation here does not merely skip a dropdown row - it
        // aborts application start, so the whole API crash-loops and nobody can even sign in.
        // One mistyped seed id should cost a warning and a missing language, not authentication.
        var languageIds = await context.Languages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(language => language.Id)
            .ToListAsync(cancellationToken);

        var knownLanguages = new HashSet<Guid>(languageIds);

        var countryIds = await context.Countries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(country => country.Id)
            .ToListAsync(cancellationToken);

        var knownCountries = new HashSet<Guid>(countryIds);
        var added = 0;

        foreach (var seed in seeds)
        {
            var id = Guid.Parse($"88888888-8888-8888-8888-88888888{seed.Link.PadLeft(4, '0')}");

            if (existingIds.Contains(id))
            {
                continue;
            }

            var countryId = Guid.Parse(CountryPrefix + seed.Country);
            var languageId = Guid.Parse(
                $"77777777-7777-7777-7777-77777777{seed.Language.PadLeft(4, '0')}");

            if (!knownCountries.Contains(countryId) || !knownLanguages.Contains(languageId))
            {
                // Said out loud rather than swallowed, because the fix is to correct the seed
                // and a silently missing language is a dropdown somebody will report as a bug.
                logger.LogWarning(
                    "Country-language link {LinkId} names country {CountryId} and language "
                    + "{LanguageId}; one of them is not in the catalogue. Skipped.",
                    id, countryId, languageId);

                continue;
            }

            await context.CountryLanguages.AddAsync(
                new CountryLanguage
                {
                    Id = id,
                    TenantId = null,
                    TenantKey = Guid.Empty,
                    BusinessUnitId = _businessUnitId,
                    CountryId = countryId,
                    LanguageId = languageId,
                    IsPrimary = seed.Primary,
                    IsOfficial = seed.Official,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    private static async Task<HashSet<Guid>> ExistingIdsAsync<TEntity>(
        DbSet<TEntity> set, CancellationToken cancellationToken)
        where TEntity : Domain.Common.GlobalMasterEntity =>
        [.. await set
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken)];
}
