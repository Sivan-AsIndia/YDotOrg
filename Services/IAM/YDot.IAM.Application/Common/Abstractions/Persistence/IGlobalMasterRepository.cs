using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the five global masters: Country, StateProvince, City, Currency and
/// TimeZoneDefinition.
///
/// ONE INTERFACE RATHER THAN FIVE, and the reason is that four of the five operations are
/// genuinely identical. Load by id, check a code is free, add, remove — the only thing that
/// varies is the table. Five hand-written repositories would be five copies of the same
/// twenty lines, and the fifth copy is where the tenant predicate quietly comes out wrong.
///
/// The generic half below is closed over <see cref="GlobalMasterEntity"/>, which is what
/// guarantees every one of those operations applies the same scope rule. What genuinely
/// differs — a country's ISO code, a state's city count, whether anything still points at a
/// currency — is written out explicitly underneath, because those ARE different questions
/// and pretending otherwise would need a predicate parameter that hides the interesting part.
///
/// EVERY READ HERE PASSES THROUGH THE SCOPED QUERY FILTER, so it returns platform rows plus
/// the caller's own and never another Organisation's. Nothing in this interface can reach
/// across a Tenant boundary, which is why none of its methods take a TenantId to filter by.
/// </summary>
public interface IGlobalMasterRepository
{
    // ---- The operations every master shares -----------------------------------------

    /// <summary>
    /// Loads one row for editing. Returns null when it does not exist OR belongs to another
    /// Organisation — the filter makes those two indistinguishable, which is the point.
    /// </summary>
    Task<TEntity?> GetByIdAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity;

    /// <summary>
    /// Whether a code is already taken inside the scope the caller is writing into.
    ///
    /// <paramref name="tenantId"/> is the scope being written to, NOT a filter on what the
    /// caller may see: null means the platform catalogue, a value means that Organisation's
    /// own rows. The two are checked separately because they are separately unique — TEN001
    /// may define a private country coded IN even though the platform already has one.
    ///
    /// <paramref name="excludeId"/> is the row being edited, so a rename that keeps its own
    /// code does not collide with itself.
    /// </summary>
    Task<bool> CodeExistsAsync<TEntity>(
        string code, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity;

    Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity;

    void Remove<TEntity>(TEntity entity) where TEntity : GlobalMasterEntity;

    // ---- Countries -------------------------------------------------------------------------

    /// <summary>A country with the counts the detail screen shows, in one round trip.</summary>
    Task<Country?> GetCountryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an ISO 3166-1 alpha-2 code is taken. Scoped exactly like
    /// <see cref="CodeExistsAsync{TEntity}"/>: two Organisations may each hold a private row
    /// for the same country, but neither may hold two.
    /// </summary>
    Task<bool> Iso2ExistsAsync(
        string iso2, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>States beneath a country. Non-zero blocks deletion.</summary>
    Task<int> CountStatesForCountryAsync(Guid countryId, CancellationToken cancellationToken);

    /// <summary>Cities beneath a country, counted directly off the denormalised column.</summary>
    Task<int> CountCitiesForCountryAsync(Guid countryId, CancellationToken cancellationToken);

    // ---- States and provinces --------------------------------------------------------------------

    Task<StateProvince?> GetStateProvinceAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Cities beneath a state. Non-zero blocks deletion.</summary>
    Task<int> CountCitiesForStateAsync(Guid stateProvinceId, CancellationToken cancellationToken);

    // ---- Cities ----------------------------------------------------------------------------------------

    Task<City?> GetCityAsync(Guid id, CancellationToken cancellationToken);

    // ---- Currencies ------------------------------------------------------------------------------------------

    Task<Currency?> GetCurrencyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Countries naming this currency as their default.
    ///
    /// Matched on CODE, not on a foreign key, because <c>Country.DefaultCurrencyCode</c> is
    /// deliberately a loose reference — see the entity for why. That means the check has to
    /// be explicit here rather than being enforced by the database, so deleting a currency
    /// cannot silently strip the default off a country.
    /// </summary>
    Task<int> CountCountriesUsingCurrencyAsync(string currencyCode, CancellationToken cancellationToken);

    // ---- Time zones ------------------------------------------------------------------------------------------

    Task<TimeZoneDefinition?> GetTimeZoneAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether an IANA key is taken inside the scope being written to.</summary>
    Task<bool> IanaKeyExistsAsync(
        string ianaKey, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>States defaulting to this zone. Non-zero blocks deletion.</summary>
    Task<int> CountStatesUsingTimeZoneAsync(Guid timeZoneId, CancellationToken cancellationToken);
}
