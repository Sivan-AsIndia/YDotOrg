using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// The address-form pickers, for every page in the platform that is not a Masters screen.
///
/// WHY THIS CONTROLLER EXISTS AT ALL, AND IT IS NOT A DUPLICATE OF <see cref="MastersController"/>.
/// That one is gated on <c>PermissionCodes.GlobalMaster.Section</c> — correctly, because it
/// serves the five ADMIN screens, and the payload it returns is shaped for administering the
/// catalogue. But a country/state/city picker is not an administrative act. It appears on user
/// creation, organisation setup, lead capture, donation entry and the campaign wizard, and the
/// people filling those in have no business holding a Masters permission.
///
/// Before this existed, the only route to the catalogue was the gated one. Any page whose user
/// lacked <c>GlobalMaster.Section</c> got a 403 where a country list should have been — which is
/// precisely why those pages had hard-coded arrays of Indian states compiled into the bundle
/// instead. The permission was doing the opposite of its job: not protecting anything (the
/// countries of the world are not a secret) while pushing five screens onto stale copies of the
/// data.
///
/// SO THE GATE HERE IS AUTHENTICATION, NOT PERMISSION. <c>ActiveUserOnly</c> and nothing more,
/// which is the same reasoning <see cref="CountriesController"/> sets out for not requiring a
/// resolved Organisation. Isolation is not weakened: every read below runs through the scoped
/// query filter, so a caller still sees the platform catalogue plus their own Organisation's
/// additions and never another Organisation's. Nothing here writes.
///
/// NOTHING HERE 404s. Every route answers 200 with a usable list. An unknown country id narrows
/// nothing rather than failing, because a dropdown that returns an error is a dropdown a person
/// cannot get past — see <see cref="GetTimeZonesAsync"/> for the case that matters most.
///
/// AND IT IS <c>[AllowedWhileOnboarding]</c>, for the reason the rest of this comment argues.
/// <c>OrganisationApprovalMiddleware</c> denies by default, so without the marker every route
/// below was refused with TENANT_NOT_APPROVED for exactly as long as an Organisation was
/// Invited, ProfileIncomplete, Submitted, UnderReview, Rejected or Resubmitted — which is
/// precisely the window in which the profile has to be filled in. The visible bug was an empty
/// Country dropdown on the one screen that cannot be completed without it: `GeoMasterService`
/// turns a failed fetch into an empty list on purpose, so the refusal arrived as a picker with
/// nothing in it and no error to explain why. Reading the catalogue is not Tenant work; it is
/// what onboarding needs in order to become Tenant work.
/// </summary>
[Route("api/v1/masters/lookups")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[AllowedWhileOnboarding]
public sealed class MasterLookupsController(GlobalMasterQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// Every active country, each carrying its default currency and primary time zone.
    ///
    /// Those two travel with the row so that choosing a country can pre-select the currency and
    /// zone WITHOUT a second call. A form that has to fetch the country's detail to learn its
    /// currency shows the person an empty currency box for as long as that round trip takes.
    /// </summary>
    [HttpGet("countries")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CountryLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCountriesAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupCountriesQuery(), cancellationToken));

    /// <summary>
    /// The active states beneath one country — the second step of the cascade.
    ///
    /// A country with no states in the catalogue answers with an empty list and a 200, not a
    /// 404. Singapore genuinely has no subdivisions, and a city-state must not make an address
    /// form unsubmittable.
    /// </summary>
    [HttpGet("states")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatesAsync(
        [FromQuery] Guid countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupStateProvincesQuery(countryId), cancellationToken));

    /// <summary>
    /// The active cities — the third step of the cascade.
    ///
    /// BOTH PARAMETERS ARE OPTIONAL AND THE STATE WINS. Pass the state for a true cascade; pass
    /// only the country for the several forms that collect a city without ever asking for a
    /// state. Passing neither returns nothing rather than every city in the catalogue, which is
    /// the one answer no address form has a use for.
    /// </summary>
    [HttpGet("cities")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MasterLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCitiesAsync(
        [FromQuery] Guid? countryId,
        [FromQuery] Guid? stateProvinceId,
        CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new LookupGeoCitiesQuery(countryId, stateProvinceId), cancellationToken));

    /// <summary>
    /// The active currencies, with one country's default flagged and sorted first.
    ///
    /// THE COUNTRY DOES NOT NARROW THIS LIST, only orders it. An Indian organisation accepting a
    /// donation in USD is ordinary rather than exceptional, and a currency picker that hides
    /// every currency but the country's own would make that impossible to record.
    /// </summary>
    [HttpGet("currencies")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CurrencyLookupResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrenciesAsync(
        [FromQuery] Guid? countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupCurrenciesQuery(countryId), cancellationToken));

    /// <summary>
    /// The active time zones, narrowed to a country's own when it has any mapped.
    ///
    /// THIS ROUTE IS THE ONE THE BRIEF TURNS ON, so it is worth being exact about what each call
    /// returns:
    ///
    /// <code>
    /// no countryId          the whole catalogue, IsCountryFiltered = false. This is the page
    ///                       that needs a time zone and never asks for a country. It is a
    ///                       supported case, not a degraded one.
    /// countryId with zones  that country's zones, primary first, IsCountryFiltered = true.
    ///                       All of them - the United States returns six, not one.
    /// countryId without     the whole catalogue, IsCountryFiltered = false. An unknown id, or a
    ///                       country nobody has mapped zones to yet. NOT a 404 and NOT an empty
    ///                       list: a required field with an empty dropdown cannot be satisfied,
    ///                       and to the person filling the form that is indistinguishable from
    ///                       the page being broken.
    /// </code>
    ///
    /// The flag is what keeps the fallback honest — it lets the form say "not narrowed to this
    /// country" rather than implying every zone in the list is observed there.
    /// </summary>
    [HttpGet("timezones")]
    [ProducesResponseType(typeof(ApiResponse<TimeZoneLookupListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeZonesAsync(
        [FromQuery] Guid? countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupTimeZonesQuery(countryId), cancellationToken));

    /// <summary>
    /// The active languages, narrowed to a country's own when it has any mapped.
    ///
    /// THE SAME THREE CASES AS <see cref="GetTimeZonesAsync"/>, and the same guarantee:
    ///
    /// <code>
    /// no countryId          the whole catalogue, IsCountryFiltered = false. The setup wizard
    ///                       and user creation both collect a language and never a country, so
    ///                       this is the ordinary case rather than the degraded one.
    /// countryId with langs  that country's languages, primary first, IsCountryFiltered = true.
    ///                       All of them - India returns its scheduled set, not just Hindi.
    /// countryId without     the whole catalogue, IsCountryFiltered = false. Never a 404 and
    ///                       never an empty list.
    /// </code>
    ///
    /// Bind an option's VALUE to the culture code rather than the id: every column that stores a
    /// language today holds "en-IN" and their APIs still take that string, so a picker keyed on
    /// the id would fail to match any record that already exists.
    /// </summary>
    [HttpGet("languages")]
    [ProducesResponseType(typeof(ApiResponse<LanguageLookupListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLanguagesAsync(
        [FromQuery] Guid? countryId, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new LookupLanguagesQuery(countryId), cancellationToken));

    /// <summary>
    /// Every picker above in one payload, for a form opening cold.
    ///
    /// ONE CALL RATHER THAN FIVE. A form that fires five parallel requests renders five times,
    /// each a little more complete, and any one of them failing leaves a dropdown empty with
    /// nothing to explain why. Pass the country and state already on the record when editing, so
    /// the cascade arrives populated rather than filling in over three further round trips.
    /// </summary>
    [HttpGet("geo")]
    [ProducesResponseType(typeof(ApiResponse<GeoLookupResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGeoAsync(
        [FromQuery] Guid? countryId,
        [FromQuery] Guid? stateProvinceId,
        CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetGeoLookupQuery(countryId, stateProvinceId), cancellationToken));
}
