using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// The address-form contract
// =====================================================================================
//
// SEPARATE FROM MasterLookupResponse ON PURPOSE. That record answers "what may I pick and may
// I edit it", which is what the five Masters ADMIN screens ask. An address form on any other
// page asks something different: having picked a country, what currency and time zone should
// I now pre-select, and what does the phone prefix become?
//
// Answering that with MasterLookupResponse means the browser picks a country, then issues a
// second call for the country's detail to learn its currency, then a third for its zones. The
// records below carry those answers on the first payload, so a country change is a local
// computation and the only network call left is the genuinely cascading one — the states.

/// <summary>
/// One option in a country picker, carrying what the rest of the form needs to react.
///
/// <see cref="DefaultCurrencyId"/> and <see cref="PrimaryTimeZoneId"/> are the reason this
/// record is not <c>MasterLookupResponse</c>: they let a form pre-select the currency and the
/// time zone the instant a country is chosen, without a round trip that the person would see
/// as a flicker.
///
/// <see cref="TimeZoneCount"/> tells the form whether the zone picker is a real choice. One
/// means it can be pre-selected and left alone; more than one — the United States has six —
/// means the person genuinely has to be asked.
/// </summary>
public sealed record CountryLookupResponse(
    Guid Id,
    string Code,
    string Name,
    string Iso2,
    string FlagEmoji,
    string? PhoneCountryCode,
    bool HasStates,
    Guid? DefaultCurrencyId,
    string? DefaultCurrencyCode,
    Guid? PrimaryTimeZoneId,
    int TimeZoneCount,
    MasterDataStatus Status,
    bool IsPlatformRow,
    int SortOrder);

/// <summary>
/// One option in a time-zone picker.
///
/// <see cref="IsPrimaryForCountry"/> is set only when the list was asked for in the context of
/// a country. On an unfiltered list it is false throughout, which is correct — "primary" has
/// no meaning without a country to be primary FOR.
/// </summary>
public sealed record TimeZoneLookupResponse(
    Guid Id,
    string Code,
    string IanaKey,
    string Name,
    string? ShortName,
    string OffsetDisplay,
    int StandardUtcOffsetMinutes,
    bool SupportsDaylightSaving,
    bool IsPrimaryForCountry,
    bool IsDefaultRecommended,
    MasterDataStatus Status,
    bool IsPlatformRow,
    int SortOrder);

/// <summary>
/// One option in a currency picker.
///
/// <see cref="Symbol"/> and <see cref="DecimalPlaces"/> travel with the option because the
/// field beside the picker has to reformat the moment the currency changes — JPY has no minor
/// unit, and an amount box still showing two decimal places after a switch to yen is wrong.
/// </summary>
public sealed record CurrencyLookupResponse(
    Guid Id,
    string Code,
    string Name,
    string? Symbol,
    int DecimalPlaces,
    bool IsDefaultForCountry,
    MasterDataStatus Status,
    bool IsPlatformRow,
    int SortOrder);

/// <summary>
/// Everything an address form needs, in one call.
///
/// WHY THE WHOLE PAYLOAD RATHER THAN FIVE ENDPOINTS. A form that opens with five parallel
/// requests renders five times, each time a little more complete, and any one of them failing
/// leaves a dropdown empty with nothing to say why. This is one request, one render.
///
/// <see cref="TimeZonesAreCountryFiltered"/> is the honest half of the graceful-degradation
/// rule. When a country was asked for and HAS mapped zones, the list is that country's and the
/// flag is true. When the country has none mapped — a Tenant's own country, or a seed gap —
/// the list falls back to the full catalogue and the flag is FALSE, so the form can say "not
/// narrowed to this country" instead of silently implying that six hundred zones are all
/// observed in Belgium. An empty dropdown is never the answer.
/// </summary>
public sealed record GeoLookupResponse(
    IReadOnlyList<CountryLookupResponse> Countries,
    IReadOnlyList<MasterLookupResponse> StateProvinces,
    IReadOnlyList<MasterLookupResponse> Cities,
    IReadOnlyList<CurrencyLookupResponse> Currencies,
    IReadOnlyList<TimeZoneLookupResponse> TimeZones,
    bool TimeZonesAreCountryFiltered,
    IReadOnlyList<LanguageLookupResponse> Languages,
    bool LanguagesAreCountryFiltered);

/// <summary>
/// One option in a language picker.
///
/// <see cref="CultureCode"/> IS THE VALUE, NOT THE ID. Every column that already stores a
/// language on this platform — <c>tenants.default_culture</c>, <c>users.preferred_language</c>,
/// the lead record's language — holds a BCP-47 string such as "en-IN", and their APIs still
/// take one. So a picker binds its option value to the culture code and the stored records keep
/// resolving; the id is carried alongside for anything that later moves to a foreign key.
///
/// <see cref="NativeName"/> travels with the row because a person choosing their OWN language is
/// looking for the word they recognise, which is rarely the English one.
/// </summary>
public sealed record LanguageLookupResponse(
    Guid Id,
    string Code,
    string CultureCode,
    string Name,
    string? NativeName,
    string DisplayLabel,
    string Iso2,
    bool IsRightToLeft,
    bool IsPrimaryForCountry,
    bool IsOfficialInCountry,
    bool IsDefaultRecommended,
    MasterDataStatus Status,
    bool IsPlatformRow,
    int SortOrder);

/// <summary>
/// A language list, plus whether it was actually narrowed to the country asked for.
///
/// THE SAME CONTRACT AS <c>TimeZoneLookupListResponse</c>, and for the same reason. A country
/// with no languages mapped — a Tenant's own country, or a seed that has not caught up — falls
/// back to the full catalogue rather than answering with an empty dropdown, and the flag is what
/// lets the form label that honestly instead of implying every language listed is spoken there.
/// </summary>
public sealed record LanguageLookupListResponse(
    IReadOnlyList<LanguageLookupResponse> Languages,
    bool IsCountryFiltered);
